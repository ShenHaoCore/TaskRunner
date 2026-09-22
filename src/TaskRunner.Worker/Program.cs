using Serilog;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Hangfire;
using TaskRunner.Worker.Services;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TaskRunner.Worker";
});

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TaskRunner.Worker"));

var logRoot = AppPaths.FindSolutionRoot(builder.Environment.ContentRootPath) ?? builder.Environment.ContentRootPath;
Directory.SetCurrentDirectory(logRoot);

builder.Services.AddTaskRunnerCore(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddTaskRunnerHangfire(builder.Configuration, builder.Environment.ContentRootPath, HangfireHostRole.Server);
builder.Services.AddHostedService<WorkerHeartbeatService>();

var host = builder.Build();
var storage = builder.Configuration.GetSection("Hangfire")["Storage"] ?? "SqlServer";
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("TaskRunner.Worker")
    .LogInformation("TaskRunner Worker 已启动，角色=Server，存储={Storage}，不监听 HTTP。", storage);
host.Run();
