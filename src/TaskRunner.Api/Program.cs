using Hangfire;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Dashboard;
using TaskRunner.Api.Filters;
using TaskRunner.Api.Hosting;
using TaskRunner.Api.Services;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TaskRunner.Api"));

var logRoot = AppPaths.FindSolutionRoot(builder.Environment.ContentRootPath) ?? builder.Environment.ContentRootPath;
Directory.SetCurrentDirectory(logRoot);

if (!builder.Environment.IsDevelopment())
{
    var adminKey = builder.Configuration.GetSection(TaskRunnerOptions.SectionName)["AdminApiKey"];
    if (string.IsNullOrWhiteSpace(adminKey))
    {
        throw new InvalidOperationException("生产环境必须配置 Hangfire:AdminApiKey。");
    }
}

builder.Services.AddTaskRunnerCore(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddTaskRunnerHangfire(builder.Configuration, builder.Environment.ContentRootPath, HangfireHostRole.Client);
builder.Services.AddTaskRunnerAdminAuth();
builder.Services.AddHostedService<RecurringJobBootstrapper>();
builder.Services.AddSingleton<HangfireMonitoringReader>();
builder.Services.AddScoped<ReadOnlyGuardFilter>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers(options => options.Filters.AddService<ReadOnlyGuardFilter>());
builder.Services.AddOpenApi();

var app = builder.Build();

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
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHangfireDashboard(dashboardPath, new DashboardOptions
{
    DashboardTitle = "任务调度中心",
    DisplayStorageConnectionString = false,
    Authorization = [new AdminDashboardAuthorizationFilter()],
    IsReadOnlyFunc = _ =>
    {
        using var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TaskRunner.Core.Services.IRuntimeSettingsStore>()
            .GetReadOnlyModeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    },
    StatsPollingInterval = 2000
});

app.MapControllers();
app.MapGet("/info", (IHostEnvironment environment) => Results.Ok(new
{
    name = "TaskRunner",
    role = "Client",
    dashboard = dashboardPath,
    scalar = environment.IsDevelopment() ? "/scalar" : null,
    tasks = "/api/tasks",
    backgroundJobs = "/api/background-jobs",
    authLogin = "/api/auth/login"
})).AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Logger.LogInformation(
    "TaskRunner Api 已启动，角色=Client，存储={Storage}，Dashboard={Dashboard}。",
    hangfireOptions.Storage,
    dashboardPath);
app.Run();
