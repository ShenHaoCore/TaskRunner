using System.Reflection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;
using TaskRunner.PlatformJobs;

namespace TaskRunner.Tests;

/// <summary>平台自带任务集（PlatformJobs 插件程序集）的发现契约。</summary>
public sealed class PlatformJobsDiscoveryTests
{
    [Fact]
    public void Catalog_ContainsAllPlatformJobs_WithExpectedContract()
    {
        var catalog = JobCatalog.CreateDefault(typeof(LogCleanupJob).Assembly);

        var jobs = catalog.RecurringJobs.ToDictionary(job => job.JobId, StringComparer.Ordinal);

        // 四个平台任务全部被发现（Core 心跳不受影响，由 JobDiscoveryTests 单独守卫）
        var logCleanup = jobs["log-cleanup"];
        Assert.Equal("0 30 3 * * *", logCleanup.Cron);
        Assert.True(logCleanup.EnabledByDefault);
        Assert.Equal("日志清理", logCleanup.JobName);

        var digest = jobs["daily-digest"];
        Assert.Equal("0 0 8 * * *", digest.Cron);
        Assert.True(digest.EnabledByDefault);

        var selfCheck = jobs["self-check"];
        Assert.Equal("0 0 6 * * *", selfCheck.Cron);
        Assert.True(selfCheck.EnabledByDefault);

        // 演示模板默认禁用：进列表、可手动触发，不进 Hangfire recurring
        var demo = jobs["demo-progress"];
        Assert.Equal("0 */5 * * * *", demo.Cron);
        Assert.False(demo.EnabledByDefault);
    }

    [Fact]
    public void JobTypes_AreResolvable_ByJobTypeName()
    {
        var catalog = JobCatalog.CreateDefault(typeof(LogCleanupJob).Assembly);

        foreach (var job in catalog.RecurringJobs)
        {
            Assert.True(JobTypeResolver.TryResolve(job.JobType, out var type), $"类型解析失败：{job.JobType}");
            Assert.Equal(job.ClrType, type);
        }
    }

    [Fact]
    public void BothHosts_InjectPlatformJobsAssembly()
    {
        // 两侧发现集不一致时，另一侧 Bootstrapper 会把任务配置当孤儿删掉——注入必须成对出现
        var apiProgram = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Api", "Program.cs"));
        var workerProgram = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Worker", "Program.cs"));
        Assert.Contains("typeof(LogCleanupJob).Assembly", apiProgram, StringComparison.Ordinal);
        Assert.Contains("typeof(LogCleanupJob).Assembly", workerProgram, StringComparison.Ordinal);
    }
}
