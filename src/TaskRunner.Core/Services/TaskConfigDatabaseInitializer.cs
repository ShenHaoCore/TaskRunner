using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Core.Services;

public sealed class TaskConfigDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskConfigDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(cancellationToken);
        logger.LogInformation("业务库迁移已应用（TaskConfigs / SyncWindows / RuntimeSettings）。");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class RuntimeSettingsBootstrapper(
    IServiceScopeFactory scopeFactory,
    ILogger<RuntimeSettingsBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsStore>();
        await settings.SyncFromConfigurationAsync(cancellationToken);
        var readOnly = await settings.GetReadOnlyModeAsync(cancellationToken);
        logger.LogInformation("运行时设置已同步，ReadOnlyMode={ReadOnly}。", readOnly);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
