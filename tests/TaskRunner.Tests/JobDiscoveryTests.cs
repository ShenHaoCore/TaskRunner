using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

public sealed class CoreBoundaryTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferenceHangfire()
    {
        var references = typeof(IRecurringJob).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference =>
            reference.Name?.Contains("Hangfire", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void CoreProject_DoesNotReferenceHangfirePackages()
    {
        var corePath = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Core", "TaskRunner.Core.csproj");
        Assert.True(File.Exists(corePath), $"找不到 Core 工程：{corePath}");
        var text = File.ReadAllText(corePath);
        Assert.DoesNotContain("Hangfire", text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class JobIdGeneratorTests
{
    [Fact]
    public void Create_StripsJobSuffixAndUsesKebabCase()
    {
        Assert.Equal("sync-data", JobIdGenerator.Create(typeof(SyncDataJob), null));
    }

    [Fact]
    public void Create_UsesExplicitId()
    {
        Assert.Equal("custom", JobIdGenerator.Create(typeof(SyncDataJob), " custom "));
    }

    private sealed class SyncDataJob;
}

public sealed class CronExpressionGuardTests
{
    [Theory]
    [InlineData("*/5 * * * * *")]
    [InlineData("0 * * * * *")]
    [InlineData("0 * * * *")]
    [InlineData("@daily")]
    public void EnsureValid_AcceptsFiveAndSixFieldExpressions(string cron)
    {
        CronExpressionGuard.EnsureValid(cron);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("* * * *")]
    [InlineData("* * * * * * *")]
    public void EnsureValid_RejectsInvalidExpressions(string cron)
    {
        Assert.Throws<ArgumentException>(() => CronExpressionGuard.EnsureValid(cron));
    }
}

public sealed class JobScannerTests
{
    [Fact]
    public void Scan_CoreContainsOnlyPlatformHeartbeat()
    {
        var jobs = RecurringJobScanner.Scan(typeof(IRecurringJob).Assembly);

        // 契约：Core 只内置平台心跳，业务任务一律经插件程序集注入。
        var job = Assert.Single(jobs);
        Assert.Equal(HeartbeatJob.JobIdValue, job.JobId);
        Assert.Equal("0 * * * * *", job.Cron);
        Assert.True(job.EnabledByDefault);
    }

    [Fact]
    public void Scan_IgnoresUnmarkedAndAbstractJobs()
    {
        var jobs = RecurringJobScanner.Scan(typeof(MarkedTestJob).Assembly);
        Assert.Contains(jobs, job => job.JobId == "marked-test" && job.JobName == "标记任务" && job.Queue == "default");
        Assert.DoesNotContain(jobs, job => job.ClrType == typeof(UnmarkedRecurringJob));
        Assert.DoesNotContain(jobs, job => job.ClrType == typeof(AbstractRecurringJob));
    }

    [Fact]
    public void Scan_CapturesDeclaredQueue()
    {
        var jobs = RecurringJobScanner.Scan(typeof(QueuedTestJob).Assembly);
        Assert.Contains(jobs, job => job.JobId == "queued-test" && job.Queue == "critical");
    }
}

public sealed class QueueNameGuardTests
{
    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("  ", "default")]
    [InlineData("critical", "critical")]
    [InlineData(" low_1 ", "low_1")]
    public void Normalize_MapsEmptyToDefault_AndTrims(string? input, string expected)
    {
        Assert.Equal(expected, QueueNameGuard.Normalize(input));
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("bad queue")]
    [InlineData("a/b")]
    public void Normalize_RejectsInvalidNames(string input)
    {
        Assert.Throws<ArgumentException>(() => QueueNameGuard.Normalize(input));
    }

    [Fact]
    public void NormalizeSet_TrimsDedupes_AndFallsBackToDefault()
    {
        // 空/缺失项按 Normalize 语义落回 default，与 Worker 订阅装配的行为一致
        Assert.Equal(["a", "b", "default"], QueueNameGuard.NormalizeSet([" a ", "b", "a", null, ""]));
        Assert.Equal(["default"], QueueNameGuard.NormalizeSet([]));
        Assert.Equal(["default"], QueueNameGuard.NormalizeSet(null));
    }

    [Fact]
    public void NormalizeSet_RejectsInvalidName()
    {
        Assert.Throws<ArgumentException>(() => QueueNameGuard.NormalizeSet(["ok", "BAD"]));
    }
}

[RecurringTask("*/15 * * * * *", Description = "测试任务", Name = "标记任务")]
public sealed class MarkedTestJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[RecurringTask("*/15 * * * * *", JobId = "queued-test", Queue = "critical")]
public sealed class QueuedTestJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UnmarkedRecurringJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[RecurringTask("0 * * * * *")]
public abstract class AbstractRecurringJob : IRecurringJob
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
