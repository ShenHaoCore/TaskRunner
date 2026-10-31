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
    public async Task RemoveMissing_EmptyDiscovery_NeverDeletes()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(CancellationToken.None);

        var discovered = new RecurringJobDescriptor("demo-job", "DemoJob", "*/5 * * * * *", "Demo.Job, Demo", typeof(string), "演示任务", true);
        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);

        // 空发现集（插件整体加载失败的保险）绝不触发删除
        var removed = await repository.RemoveMissingAsync([], CancellationToken.None);
        Assert.Empty(removed);
        Assert.NotNull(await repository.FindAsync("demo-job", CancellationToken.None));
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

        var disabled = new RecurringJobDescriptor("sample-daily", "示例每日任务", "0 0 9 * * *", "T, A", typeof(string), "旧描述", false);
        await repository.SyncDiscoveredAsync([disabled], CancellationToken.None);
        var before = await repository.FindAsync("sample-daily", CancellationToken.None);
        Assert.NotNull(before);
        Assert.False(before.IsEnabled);
        Assert.Equal(before.CreatedAt, before.UpdatedAt);

        var enabled = disabled with { EnabledByDefault = true, Description = "新描述" };
        await repository.SyncDiscoveredAsync([enabled], CancellationToken.None);

        var after = await repository.FindAsync("sample-daily", CancellationToken.None);
        Assert.NotNull(after);
        Assert.True(after.IsEnabled);
        Assert.Equal("新描述", after.Description);
    }

    [Fact]
    public async Task RemoveMissing_DeferredWithinGraceWindow_RemovesAfterGrace()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
            await repository.InitializeAsync(CancellationToken.None);

            await repository.SyncDiscoveredAsync(
            [
                new RecurringJobDescriptor("keep", "Keep", "*/5 * * * * *", "T, A", typeof(string), null, true),
                new RecurringJobDescriptor("gone", "Gone", "*/5 * * * * *", "T, A", typeof(string), null, true)
            ], CancellationToken.None);
        }

        // 宽限期内（如滚动升级中旧进程的发现集）缺失任务只观察不删除
        await using (var scope = _provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
            var removed = await repository.RemoveMissingAsync(["keep"], CancellationToken.None);
            Assert.Empty(removed);
            Assert.NotNull(await repository.FindAsync("gone", CancellationToken.None));
        }

        // 回拨超过宽限期 → 真废弃，清理
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
            await db.TaskConfigs
                .Where(row => row.JobId == "gone")
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(row => row.UpdatedAt, DateTime.UtcNow.AddMinutes(-TaskRunner.Core.Services.TaskConfigRepository.OrphanGraceMinutes - 1)),
                    CancellationToken.None);
        }

        await using (var scope = _provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
            var removed = await repository.RemoveMissingAsync(["keep"], CancellationToken.None);
            Assert.Equal(["gone"], removed);
            Assert.Null(await repository.FindAsync("gone", CancellationToken.None));
        }
    }

    [Fact]
    public async Task UpdateAsync_UsesPreloadedRow_AndPersists()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.InitializeAsync(CancellationToken.None);

        var discovered = new RecurringJobDescriptor("demo-job", "DemoJob", "*/5 * * * * *", "Demo.Job, Demo", typeof(string), "演示任务", true);
        await repository.SyncDiscoveredAsync([discovered], CancellationToken.None);

        var row = await repository.FindAsync("demo-job", CancellationToken.None);
        Assert.NotNull(row);
        var updated = await repository.UpdateAsync(row, r => r.IsEnabled = false, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);
        var stored = await repository.FindAsync("demo-job", CancellationToken.None);
        Assert.False(stored!.IsEnabled);
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

        // 孤儿已存在超过宽限期（模拟代码删除后久未运行的场景）
        await using (var updateScope = _provider.CreateAsyncScope())
        {
            var db = updateScope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
            await db.TaskConfigs
                .Where(row => row.JobId == "gone")
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(row => row.UpdatedAt, DateTime.UtcNow.AddMinutes(-TaskRunner.Core.Services.TaskConfigRepository.OrphanGraceMinutes - 1)),
                    CancellationToken.None);
        }

        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyRepository = verifyScope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        var removed = await verifyRepository.RemoveMissingAsync(["keep"], CancellationToken.None);
        Assert.Equal(["gone"], removed);
        Assert.Null(await verifyRepository.FindAsync("gone", CancellationToken.None));
        Assert.NotNull(await verifyRepository.FindAsync("keep", CancellationToken.None));
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) { File.Delete(_path); }
    }
}
