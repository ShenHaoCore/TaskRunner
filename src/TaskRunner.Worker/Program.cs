using Microsoft.Extensions.Options;
using Serilog;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;
using TaskRunner.Hangfire;
using TaskRunner.PlatformJobs;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddSharedConfiguration(builder.Environment.EnvironmentName);
AppPaths.AbsolutizeSerilogFilePaths(builder.Configuration);

builder.Services.AddWindowsService(options => { options.ServiceName = "TaskRunner.Worker"; });
// Linux systemd 部署（Type=notify）；非对应环境两者均为 no-op。
builder.Services.AddSystemd();
builder.Services.AddSerilog((services, configuration) => configuration.ReadFrom.Configuration(builder.Configuration).ReadFrom.Services(services).Enrich.FromLogContext().Enrich.WithProperty("Application", "TaskRunner.Worker"));

// 平台自带任务集经插件程序集注入（与 Api 同一程序集，两侧发现集一致才能正确同步配置）。
builder.Services.AddTaskRunnerCore(builder.Configuration, typeof(LogCleanupJob).Assembly);
builder.Services.AddTaskRunnerHangfire(builder.Configuration, HangfireHostRole.Server);
// Worker 侧同样跑「迁移 → 同步 recurring → 清理残留」：Api 故障/缺席时 Worker 独立完成协调（advisory lock 串行化）。
builder.Services.AddHostedService<RecurringJobBootstrapper>();

var host = builder.Build();
var hangfireOptions = host.Services.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TaskRunner.Worker").LogInformation(
    "TaskRunner Worker 已启动，角色=Server，存储={Storage}，不监听 HTTP。",
    hangfireOptions.Storage);
host.Run();
