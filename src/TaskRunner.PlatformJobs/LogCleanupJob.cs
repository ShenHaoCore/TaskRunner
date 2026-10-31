using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.PlatformJobs;

/// <summary>
/// 日志清理：删除超过保留期的滚动日志文件，防磁盘缓慢膨胀。
/// 目录经 <see cref="LogDirectories"/> 解析；带互斥锁防与手动触发/多 Worker 并发删除。
/// </summary>
[RecurringTask("0 30 3 * * *", JobId = LogCleanupJob.JobIdValue, Name = "日志清理",
    Description = "每天 03:30 删除超过保留期的滚动日志文件（保留天数 Hangfire:LogRetentionDays，默认 14）。")]
[ExclusiveExecution(300)]
public sealed class LogCleanupJob(
    IConfiguration configuration,
    IOptions<TaskRunnerOptions> options,
    IJobProgress progress,
    ILogger<LogCleanupJob> logger) : IRecurringJob
{
    public const string JobIdValue = "log-cleanup";

    private static readonly string[] LogPatterns = ["*.log", "*.txt"];

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, options.Value.LogRetentionDays);
        var directories = LogDirectories.Resolve(configuration);
        progress.WriteLine($"日志清理开始：保留 {retentionDays} 天，扫描 {directories.Count} 个目录。");
        progress.SetProgress(10);

        var scanned = 0;
        var deleted = 0;
        var failed = 0;
        var freedBytes = 0L;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in LogPatterns
                         .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                scanned++;
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc >= cutoff)
                    {
                        continue;
                    }

                    var bytes = info.Length;
                    info.Delete();
                    freedBytes += bytes;
                    deleted++;
                }
                catch (Exception ex)
                {
                    // 单文件被占用/只读不打断整轮清扫（该任务的目的就是持续防膨胀）
                    failed++;
                    logger.LogWarning(ex, "日志文件删除失败，跳过：{File}", file);
                }
            }
        }

        var freedMb = freedBytes / 1024.0 / 1024.0;
        progress.SetProgress(100);
        progress.WriteLine($"日志清理完成：扫描 {scanned} 个，删除 {deleted} 个，失败 {failed} 个，释放 {freedMb:F1} MB。");
        logger.LogInformation(
            "日志清理完成：目录 {Directories}，扫描 {Scanned}，删除 {Deleted}，失败 {Failed}，释放 {FreedMb:F1} MB。",
            string.Join("; ", directories), scanned, deleted, failed, freedMb);
        return Task.CompletedTask;
    }
}
