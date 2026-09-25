using Hangfire;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TaskRunner.Api.Dashboard;
using TaskRunner.Core.Common;
using TaskRunner.Hangfire;

namespace TaskRunner.Api.Hosting;

public static class WebApplicationExtensions
{
    /// <summary>
    /// 装配中间件管道与端点：开发文档、请求日志、静态文件、鉴权、Hangfire Dashboard 与业务 API。
    /// </summary>
    public static WebApplication UseTaskRunnerPipeline(this WebApplication app, HangfireHostRole hangfireRole)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options => options.WithTitle("TaskRunner API"));
        }

        var hangfireOptions = app.Services.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;
        var dashboardPath = hangfireOptions.NormalizeDashboardPath();

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
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = static context =>
            {
                if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    context.Context.Response.ContentType = "text/html; charset=utf-8";
                }
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHangfireDashboard(dashboardPath, new DashboardOptions
        {
            DashboardTitle = "任务调度中心",
            DisplayStorageConnectionString = false,
            Authorization = [new AdminDashboardAuthorizationFilter()],
            IsReadOnlyFunc = _ => hangfireOptions.ReadOnlyMode,
            StatsPollingInterval = 2000
        });

        app.MapControllers();
        app.MapGet("/info", (IHostEnvironment environment) => Results.Ok(new
        {
            name = "TaskRunner",
            role = hangfireRole.ToString(),
            readOnly = hangfireOptions.ReadOnlyMode,
            dashboard = dashboardPath,
            scalar = environment.IsDevelopment() ? "/scalar" : null,
            bilibiliLogin = "/bilibili-login.html",
            tasks = "/api/tasks",
            authLogin = "/api/auth/login"
        })).AllowAnonymous();

        app.MapGet("/bilibili-login", () => Results.Redirect("/bilibili-login.html")).AllowAnonymous();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        return app;
    }
}
