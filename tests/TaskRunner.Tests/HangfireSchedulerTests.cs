using Hangfire;
using Hangfire.Common;
using Hangfire.MemoryStorage;
using TaskRunner.Hangfire;

namespace TaskRunner.Tests;

/// <summary>
/// PruneRecurringExcept 真实现（MemoryStorage 直建，不经 TestHost 的 RecordingScheduler 替身）：
/// 保留清单之外的残留 recurring 被清理，且幂等。
/// MemoryStorage 不支持按队列注册 recurring（生产为 PostgreSqlStorage），故用管理器直接种子。
/// </summary>
public sealed class HangfireSchedulerTests
{
    [Fact]
    public void PruneRecurringExcept_RemovesResidual_AndKeepsListed()
    {
        var storage = new MemoryStorage();
        var manager = new RecurringJobManager(storage, JobFilterProviders.Providers);
        var options = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };
        var method = typeof(Probe).GetMethod(nameof(Probe.Run))!;
        manager.AddOrUpdate("keep-a", new Job(method), "0 0 * * * *", options);
        manager.AddOrUpdate("residual", new Job(method), "0 0 * * * *", options);

        var scheduler = new HangfireTaskScheduler(
            new BackgroundJobClient(storage),
            manager,
            storage);

        var removed = scheduler.PruneRecurringExcept(["keep-a"]);

        Assert.Equal(["residual"], removed);
        Assert.Empty(scheduler.PruneRecurringExcept(["keep-a"])); // 幂等：残留清零后不再新增删除
    }

    private static class Probe
    {
        public static void Run()
        {
        }
    }
}
