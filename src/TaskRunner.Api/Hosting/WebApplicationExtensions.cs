using System.Reflection;
using Hangfire;
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
    /// <summary>
    /// 装配中间件管道与端点：请求日志、API 文档、鉴权守卫、Hangfire Dashboard 与业务 API。
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

        app.UseExceptionHandler();
        app.UseStaticFiles();
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
                    && context.Request.Headers.Accept.ToString()
                        .Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var returnUrl = context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
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
            StatsPollingInterval = 2000
        });

        app.MapControllers();
        app.MapGet("/info", (HangfireMonitoringReader reader) =>
        {
            DateTime? heartbeatLast = null;
            DateTime? heartbeatNext = null;
            if (hangfireOptions.HeartbeatEnabled)
            {
                var heartbeat = reader.ReadHeartbeat();
                heartbeatLast = heartbeat?.LastExecution;
                heartbeatNext = heartbeat?.NextExecution;
            }

            return Results.Ok(new
            {
                name = "TaskRunner",
                // 发布时由 CI 注入 -p:InformationalVersion=版本号（随 tag），本机构建回退程序集版本
                version = typeof(WebApplicationExtensions).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(WebApplicationExtensions).Assembly.GetName().Version?.ToString(),
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

        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        return app;
    }
}
