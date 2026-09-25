using Microsoft.Extensions.Options;
using Serilog;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Filters;
using TaskRunner.Api.Hosting;
using TaskRunner.Api.Services;
using TaskRunner.Bilibili;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Hangfire;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSharedConfiguration(builder.Environment.EnvironmentName);

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

// 开发：单进程 Combined（不必再开 Worker）；生产 Api 仅 Client，执行靠 Worker 服务。
var hangfireRole = builder.Environment.IsDevelopment() ? HangfireHostRole.Combined : HangfireHostRole.Client;

builder.Services.AddTaskRunnerCore(
    builder.Configuration,
    typeof(TaskRunner.Bilibili.Jobs.BilibiliDailyJob).Assembly);
builder.Services.AddBilibili(builder.Configuration);
builder.Services.AddTaskRunnerHangfire(builder.Configuration, hangfireRole);
builder.Services.AddTaskRunnerAdminAuth();
builder.Services.AddHostedService<RecurringJobBootstrapper>();
builder.Services.AddSingleton<HangfireMonitoringReader>();
builder.Services.AddScoped<ReadOnlyGuardFilter>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers(options => options.Filters.AddService<ReadOnlyGuardFilter>());
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseTaskRunnerPipeline(hangfireRole);

var hangfireOptions = app.Services.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;
app.Logger.LogInformation(
    "TaskRunner Api 已启动，角色={Role}，存储={Storage}，Dashboard={Dashboard}。",
    hangfireRole,
    hangfireOptions.Storage,
    hangfireOptions.NormalizeDashboardPath());
app.Run();
