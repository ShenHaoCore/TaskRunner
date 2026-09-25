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

        var discovered = new RecurringJobDescriptor("demo-job", "DemoJob", "*/5 * * * * *", "Demo.Job, Demo", typeof(string), "演示任务", true);

        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);
        var updated = await repository.UpdateCronAsync("demo-job", "0 * * * * *", CancellationToken.None);
        await repository.SetEnabledAsync("demo-job", false, CancellationToken.None);
        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);

        var stored = await repository.FindAsync("demo-job", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.NotNull(stored);
        Assert.Equal("0 * * * * *", stored.CronExpr);
        Assert.False(stored.IsEnabled);
        Assert.Equal("演示任务", stored.Description);
    }

    [Fact]
    public async Task Sync_EnablesUntouchedJobWhenDefaultBecomesEnabled()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(CancellationToken.None);

        var disabled = new RecurringJobDescriptor("bilibili-daily", "B站每日任务", "0 0 9 * * *", "T, A", typeof(string), "旧描述", false);
        await repository.SyncDiscoveredAsync([disabled], CancellationToken.None);
        var before = await repository.FindAsync("bilibili-daily", CancellationToken.None);
        Assert.NotNull(before);
        Assert.False(before.IsEnabled);
        Assert.Equal(before.CreatedAt, before.UpdatedAt);

        var enabled = disabled with { EnabledByDefault = true, Description = "新描述" };
        await repository.SyncDiscoveredAsync([enabled], CancellationToken.None);

        var after = await repository.FindAsync("bilibili-daily", CancellationToken.None);
        Assert.NotNull(after);
        Assert.True(after.IsEnabled);
        Assert.Equal("新描述", after.Description);
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
        if (File.Exists(_path)) { File.Delete(_path); }
    }
}
