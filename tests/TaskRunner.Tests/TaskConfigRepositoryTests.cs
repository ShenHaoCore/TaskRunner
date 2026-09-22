using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

public sealed class TaskConfigRepositoryTests : IDisposable
{
    private readonly string _path;
    private readonly ServiceProvider _provider;

    public TaskConfigRepositoryTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"taskrunner-tests-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<TaskRunnerDbContext>(options => options.UseSqlite($"Data Source={_path}"));
        services.AddScoped<ITaskConfigRepository, TaskConfigRepository>();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Sync_PreservesOperatorCronAndEnabledFlag()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(CancellationToken.None);

        var discovered = new RecurringJobDescriptor(
            "sync-data",
            "SyncDataJob",
            "*/5 * * * * *",
            "TaskRunner.Core.Jobs.Implementations.SyncDataJob, TaskRunner.Core",
            typeof(string),
            "每5秒同步一次数据",
            true);

        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);
        var updated = await repository.UpdateCronAsync("sync-data", "0 * * * * *", CancellationToken.None);
        await repository.SetEnabledAsync("sync-data", false, CancellationToken.None);
        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);

        var stored = await repository.FindAsync("sync-data", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.NotNull(stored);
        Assert.Equal("0 * * * * *", stored.CronExpr);
        Assert.False(stored.IsEnabled);
        Assert.Equal("每5秒同步一次数据", stored.Description);
    }

    [Fact]
    public async Task RemoveMissing_DeletesOrphanConfigs()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(CancellationToken.None);

        await repository.SyncDiscoveredAsync(
        [
            new RecurringJobDescriptor("keep", "Keep", "*/5 * * * * *", "T, A", typeof(string), null, true),
            new RecurringJobDescriptor("gone", "Gone", "*/5 * * * * *", "T, A", typeof(string), null, true)
        ], CancellationToken.None);

        var removed = await repository.RemoveMissingAsync(["keep"], CancellationToken.None);
        Assert.Equal(["gone"], removed);
        Assert.Null(await repository.FindAsync("gone", CancellationToken.None));
        Assert.NotNull(await repository.FindAsync("keep", CancellationToken.None));
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
