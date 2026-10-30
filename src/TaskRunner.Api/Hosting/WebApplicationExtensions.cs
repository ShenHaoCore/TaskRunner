using System.Net;
using System.Reflection;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Dashboard;
using TaskRunner.Api.Services;
using TaskRunner.Core.Common;
using TaskRunner.Hangfire;

namespace TaskRunner.Api.Hosting;

public static class WebApplicationExtensions
{
    /// <summary>Hangfire Dashboard 页面内自动刷新统计的间隔（毫秒）。</summary>
    internal const int DashboardStatsPollingIntervalMs = 2000;

    /// <summary>
    /// 装配中间件管道与端点：请求日志、错误页、API 文档、鉴权守卫、Hangfire Dashboard 与业务 API。
    /// </summary>
    public static WebApplication UseTaskRunnerPipeline(this WebApplication app, HangfireHostRole hangfireRole)
    {
        var hangfireOptions = app.Services.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;
        var dashboardPath = hangfireOptions.NormalizeDashboardPath();

        // 文档端点全环境映射；生产由鉴权后的守卫要求管理员凭据（会话 Cookie / Header / Basic）。
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle("TaskRunner API"));

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0.0} ms";
            options.GetLevel = (context, _, exception) =>
            {
                if (exception is not null || context.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                var path = context.Request.Path.Value ?? string.Empty;
                if (path.StartsWith(dashboardPath, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase))
                {
                    return LogEventLevel.Debug;
                }

                return LogEventLevel.Information;
            };
        });

        // 未捕获异常 → /error 重执行；无端点命中的 4xx（如 404）→ /error/{code}。
        // ErrorController 按 /api 前缀返回 ProblemDetails JSON，页面请求渲染品牌化错误页。
        app.UseExceptionHandler("/error");
        app.UseStatusCodePagesWithReExecute("/error/{0}");

        // 反代（nginx 同机）下取真实客户端 IP：登录限流按真实 IP 分桶，日志与 Secure Cookie 也依赖它。
        // KnownProxies 仅放行本机回环，直连部署时外来 Forwarded 头一律不信任。
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwarded.KnownProxies.Clear();
        forwarded.KnownProxies.Add(IPAddress.Loopback);
        forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
        app.UseForwardedHeaders(forwarded);

        // 基础安全响应头：应用端兜底（防点击劫持），反代可按部署文档再收紧完整 CSP。
        app.Use((context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
            return next();
        });

        // 静态资源带版本号（内容寻址），可长缓存
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = static ctx =>
                ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable"
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        if (!app.Environment.IsDevelopment())
        {
            app.Use((context, next) =>
            {
                var path = context.Request.Path;
                if (path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase))
                {
                    if (!AdminCredentials.IsAuthorized(context, hangfireOptions, isDevelopment: false))
                    {
                        context.Response.Headers.WWWAuthenticate = "Basic realm=\"TaskRunner\"";
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                }

                // Dashboard：未登录的浏览器请求直接跳登录页（登录后经 returnUrl 回到原页面）；
                // 机器请求（Accept 非 text/html）继续由 Dashboard 过滤器回 401 + Basic。
                if (path.StartsWithSegments(dashboardPath, StringComparison.OrdinalIgnoreCase)
                    && context.User.Identity?.IsAuthenticated != true
                    && !AdminCredentials.TryMatchRequest(context.Request, hangfireOptions.AdminApiKey)
                    && BrowserRedirect.IsBrowserRequest(context.Request))
                {
                    BrowserRedirect.RedirectToLogin(context.Response, context.Request);
                    return Task.CompletedTask;
                }

                return next();
            });
        }

        app.MapHangfireDashboard(dashboardPath, new DashboardOptions
        {
            DashboardTitle = "任务调度中心",
            DisplayStorageConnectionString = false,
            Authorization = [new AdminDashboardAuthorizationFilter()],
            IsReadOnlyFunc = _ => hangfireOptions.ReadOnlyMode,
            StatsPollingInterval = DashboardStatsPollingIntervalMs
        });

        app.MapControllers();
        app.MapHealthChecks("/health");
        app.MapGet("/info", (HangfireMonitoringReader reader, HttpContext http) =>
        {
            DateTime? heartbeatLast = null;
            DateTime? heartbeatNext = null;
            if (hangfireOptions.HeartbeatEnabled)
            {
                var heartbeat = reader.ReadHeartbeat();
                heartbeatLast = heartbeat?.LastExecution;
                heartbeatNext = heartbeat?.NextExecution;
            }

            var version = typeof(WebApplicationExtensions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(WebApplicationExtensions).Assembly.GetName().Version?.ToString();

            // 匿名（生产）只保留最小暴露；完整信息需管理员凭据（首页按登录态取用）
            var authenticated = app.Environment.IsDevelopment()
                || AdminCredentials.IsAuthorized(http, hangfireOptions, isDevelopment: false);
            if (!authenticated)
            {
                return Results.Ok(new
                {
                    name = "TaskRunner",
                    status = "ok",
                    version = version,
                    tasks = "/api/tasks",
                    authLogin = "/api/auth/login"
                });
            }

            return Results.Ok(new
            {
                name = "TaskRunner",
                status = "ok",
                // 发布时由 CI 注入 -p:InformationalVersion=版本号（随 tag），本机构建回退程序集版本
                version = version,
                environment = app.Environment.EnvironmentName,
                role = hangfireRole.ToString(),
                readOnly = hangfireOptions.ReadOnlyMode,
                dashboard = dashboardPath,
                scalar = "/scalar",
                heartbeat = new
                {
                    enabled = hangfireOptions.HeartbeatEnabled,
                    lastExecution = heartbeatLast,
                    nextExecution = heartbeatNext
                },
                tasks = "/api/tasks",
                authLogin = "/api/auth/login"
            });
        }).AllowAnonymous();


        return app;
    }
}
