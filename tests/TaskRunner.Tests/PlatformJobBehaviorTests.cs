using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.PlatformJobs;

namespace TaskRunner.Tests;

/// <summary>平台自带任务的执行行为（进程内直调，IJobProgress 用记录式替身）。</summary>
public sealed class PlatformJobBehaviorTests
{
    private readonly RecordingProgress _progress = new();

    [Fact]
    public async Task LogCleanup_DeletesExpired_KeepsRecent()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), $"tr-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDirectory);
        try
        {
            var expired = Path.Combine(logDirectory, "app-20260101.log");
            var recent = Path.Combine(logDirectory, "app-20260102.log");
            await File.WriteAllTextAsync(expired, "old");
            await File.WriteAllTextAsync(recent, "new");
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddHours(-1));

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = Path.Combine(logDirectory, "app-.log")
            }).Build();

            var job = new LogCleanupJob(configuration, Options.Create(new TaskRunnerOptions { LogRetentionDays = 14 }), _progress, NullLogger<LogCleanupJob>.Instance);
            await job.ExecuteAsync(CancellationToken.None);

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(recent));
            Assert.Contains(_progress.Lines, line => line.Contains("删除 1 个", StringComparison.Ordinal));
            Assert.Equal(100, _progress.LastPercent);
        }
        finally
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SelfCheck_ReportsChecklist_WithoutThrowing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tr-selfcheck-{Guid.NewGuid():N}.db");
        var logDirectory = Path.Combine(Path.GetTempPath(), $"tr-selfcheck-logs-{Guid.NewGuid():N}");
        var optionsBuilder = new DbContextOptionsBuilder<TaskRunnerDbContext>().UseSqlite($"Data Source={dbPath}");
        await using var db = new TaskRunnerDbContext(optionsBuilder.Options);
        await db.Database.EnsureCreatedAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:path"] = Path.Combine(logDirectory, "self-check-.log")
        }).Build();
        var options = new TaskRunnerOptions { HeartbeatEnabled = true };

        try
        {
            var job = new PlatformSelfCheckJob(db, configuration, Options.Create(options), _progress, NullLogger<PlatformSelfCheckJob>.Instance);
            var exception = await Record.ExceptionAsync(() => job.ExecuteAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Contains(_progress.Lines, line => line.Contains("业务库连通", StringComparison.Ordinal));
            Assert.Contains(_progress.Lines, line => line.Contains("日志目录可写", StringComparison.Ordinal));
            Assert.Contains(_progress.Lines, line => line.Contains("自检通过", StringComparison.Ordinal));
            // 探针文件不留残留（写成功但删除前出异常的路径由 finally 兜底）
            Assert.Empty(Directory.GetFiles(logDirectory, ".self-check-*.tmp"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
            if (Directory.Exists(logDirectory)) { Directory.Delete(logDirectory, recursive: true); }
        }
    }

    [Fact]
    public async Task LogCleanup_SkipsUndeletableFile_ContinuesOthers()
    {
        if (!OperatingSystem.IsWindows())
        {
            // POSIX 下只读属性不影响删除，该场景仅在 Windows 可复现
            return;
        }

        var logDirectory = Path.Combine(Path.GetTempPath(), $"tr-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDirectory);
        var deletable = Path.Combine(logDirectory, "a.log");
        var locked = Path.Combine(logDirectory, "b.log");
        try
        {
            await File.WriteAllTextAsync(deletable, "old-a");
            await File.WriteAllTextAsync(locked, "old-b");
            File.SetLastWriteTimeUtc(deletable, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-30));
            File.SetAttributes(locked, FileAttributes.ReadOnly);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = Path.Combine(logDirectory, "app-.log")
            }).Build();

            var job = new LogCleanupJob(configuration, Options.Create(new TaskRunnerOptions { LogRetentionDays = 14 }), _progress, NullLogger<LogCleanupJob>.Instance);
            var exception = await Record.ExceptionAsync(() => job.ExecuteAsync(CancellationToken.None));

            Assert.Null(exception); // 单文件失败不打断整轮
            Assert.False(File.Exists(deletable));
            Assert.True(File.Exists(locked));
            Assert.Contains(_progress.Lines, line => line.Contains("删除 1 个", StringComparison.Ordinal) && line.Contains("失败 1 个", StringComparison.Ordinal));
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DailyDigest_WritesStatistics_AndFailures()
    {
        var insights = new FakeInsights();
        insights.Failures.Add(new RecentExecution("demo-job", "演示模板", "Failed", DateTime.UtcNow.AddHours(-2), "boom: 校验不通过", null));

        var job = new DailyDigestJob(insights, _progress, NullLogger<DailyDigestJob>.Instance);
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Contains(_progress.Lines, line => line.Contains("成功 10", StringComparison.Ordinal) && line.Contains("失败 2", StringComparison.Ordinal));
        Assert.Contains(_progress.Lines, line => line.Contains("demo-job", StringComparison.Ordinal) && line.Contains("boom", StringComparison.Ordinal));
        Assert.Equal(100, _progress.LastPercent);
    }

    [Fact]
    public async Task DailyDigest_ZeroFailures_ShowsHealthyLine()
    {
        var job = new DailyDigestJob(new FakeInsights(), _progress, NullLogger<DailyDigestJob>.Instance);
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Contains(_progress.Lines, line => line.Contains("最近失败：无", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoProgress_Completes_AndStepsProgress()
    {
        var job = new DemoProgressJob(_progress) { FailRate = 0 };
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(100, _progress.LastPercent);
        Assert.Contains(_progress.Lines, line => line.Contains("演示完成", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoProgress_FailRateOne_RethrowsForRetryChain()
    {
        var job = new DemoProgressJob(_progress) { FailRate = 1 };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(CancellationToken.None));

        Assert.Contains("演示失败", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DemoProgress_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var job = new DemoProgressJob(_progress);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
    }

    private sealed class RecordingProgress : IJobProgress
    {
        public List<string> Lines { get; } = [];

        public int LastPercent { get; private set; }

        public void WriteLine(string message) => Lines.Add(message);

        public void SetProgress(int percent) => LastPercent = Math.Clamp(percent, 0, 100);
    }

    private sealed class FakeInsights : IJobInsights
    {
        public List<RecentExecution> Failures { get; } = [];

        public JobStatistics GetStatistics() => new(10, 2, 1, 1, 0, 4, 1);

        public IReadOnlyList<RecentExecution> RecentFailures(int limit) => Failures.Take(limit).ToList();
    }
}
