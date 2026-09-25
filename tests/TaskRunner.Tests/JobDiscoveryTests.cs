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
        var corePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TaskRunner.Core", "TaskRunner.Core.csproj"));
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
    public void Scan_CoreHasNoBuiltInJobs()
    {
        var jobs = RecurringJobScanner.Scan(typeof(IRecurringJob).Assembly);
        Assert.Empty(jobs);
    }

    [Fact]
    public void Scan_IgnoresUnmarkedAndAbstractJobs()
    {
        var jobs = RecurringJobScanner.Scan(typeof(MarkedTestJob).Assembly);
        Assert.Contains(jobs, job => job.JobId == "marked-test" && job.JobName == "标记任务");
        Assert.DoesNotContain(jobs, job => job.ClrType == typeof(UnmarkedRecurringJob));
        Assert.DoesNotContain(jobs, job => job.ClrType == typeof(AbstractRecurringJob));
    }
}

[RecurringTask("*/15 * * * * *", Description = "测试任务", Name = "标记任务")]
public sealed class MarkedTestJob : IRecurringJob
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
