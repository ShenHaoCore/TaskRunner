using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;
using Xunit;

namespace TaskRunner.Tests;

/// <summary>
/// PostgreSQL 特有并发路径的集成测试：xmin 冲突重载重试、advisory lock 启动串行化。
/// SQLite 无法覆盖这两条路径，需要真实 PG。默认跳过（直接返回）；
/// 设置环境变量 TASKRUNNER_TEST_PG 为专用测试库连接串后启用，例如：
///   $env:TASKRUNNER_TEST_PG = "Host=127.0.0.1;Database=taskrunner_test;Username=taskrunner;Password=***"
/// 注意：会对该库执行 EF 迁移，勿指向生产库。
/// </summary>
public sealed class PostgresConcurrencyTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("TASKRUNNER_TEST_PG");

    private static DbContextOptions<TaskRunnerDbContext> CreateOptions(string connectionString)
        => new DbContextOptionsBuilder<TaskRunnerDbContext>().UseNpgsql(connectionString).Options;

    [Fact]
    public async Task ConcurrentUpdates_SecondWriterReloadsAndRetriesOnXminConflict()
    {
        var connectionString = ConnectionString;
        if (connectionString is null)
        {
            return; // 未配置 PG 测试库时跳过
        }

        var options = CreateOptions(connectionString);
        var jobId = $"pg-concurrency-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        // 两个上下文先后读同一行，再分别写——后写者持过期 xmin，必然触发冲突重试。
        await using var setup = new TaskRunnerDbContext(options);
        await setup.Database.MigrateAsync();
        setup.TaskConfigs.Add(new TaskConfig
        {
            JobId = jobId, JobName = jobId, CronExpr = "0 0 * * *", JobType = "Demo",
            Queue = "default", IsEnabled = true, CreatedAt = now, UpdatedAt = now
        });
        await setup.SaveChangesAsync();

        try
        {
            await using var db1 = new TaskRunnerDbContext(options);
            await using var db2 = new TaskRunnerDbContext(options);
            var repo1 = new TaskConfigRepository(db1);
            var repo2 = new TaskConfigRepository(db2);
            var row1 = await repo1.FindAsync(jobId, CancellationToken.None);
            var row2 = await repo2.FindAsync(jobId, CancellationToken.None);
            Assert.NotNull(row1);
            Assert.NotNull(row2);

            // 先写者成功后，后写者的 xmin 已过期：必须经重载重试成功，而不是抛并发异常。
            await repo1.UpdateAsync(row1!, row => row.CronExpr = "0 1 * * *", CancellationToken.None);
            var second = await repo2.UpdateAsync(row2!, row => row.CronExpr = "0 2 * * *", CancellationToken.None);

            Assert.NotNull(second);
            Assert.Equal("0 2 * * *", second!.CronExpr);

            await using var verify = new TaskRunnerDbContext(options);
            var persisted = await verify.TaskConfigs.SingleAsync(item => item.JobId == jobId);
            Assert.Equal("0 2 * * *", persisted.CronExpr);
        }
        finally
        {
            await using var cleanup = new TaskRunnerDbContext(options);
            await cleanup.TaskConfigs.Where(item => item.JobId == jobId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task BootstrapLock_SerializesConcurrentBodies()
    {
        var connectionString = ConnectionString;
        if (connectionString is null)
        {
            return; // 未配置 PG 测试库时跳过
        }

        var options = CreateOptions(connectionString);
        await using var db1 = new TaskRunnerDbContext(options);
        await using var db2 = new TaskRunnerDbContext(options);
        await db1.Database.MigrateAsync();

        var current = 0;
        var maxObserved = 0;
        async Task Body()
        {
            var entered = Interlocked.Increment(ref current);
            InterlockedMax(ref maxObserved, entered);
            await Task.Delay(200); // 持锁期间制造重叠窗口：无锁时 maxObserved 必为 2
            Interlocked.Decrement(ref current);
        }

        await Task.WhenAll(
            new TaskConfigRepository(db1).RunUnderBootstrapLockAsync(Body, CancellationToken.None),
            new TaskConfigRepository(db2).RunUnderBootstrapLockAsync(Body, CancellationToken.None));

        Assert.Equal(1, maxObserved);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int snapshot;
        while (value > (snapshot = Volatile.Read(ref location)))
        {
            if (Interlocked.CompareExchange(ref location, value, snapshot) == snapshot)
            {
                break;
            }
        }
    }
}
