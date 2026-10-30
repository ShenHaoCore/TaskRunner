using System.Diagnostics;
using Hangfire;
using Hangfire.Console;
using Hangfire.Console.Progress;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

/// <summary>
/// Hangfire 入口：必须带 PerformContext，Console 才能写入作业详情页。
/// 支持 Hangfire:MaxJobRuntimeMinutes 运行时限（0=不限），超时以取消令牌通知作业。
/// </summary>
public sealed class HangfireJobGateway(
    JobExecutor executor,
    JobProgressContext progressContext,
    IOptions<TaskRunnerOptions> options,
    ILogger<HangfireJobGateway> logger)
{
    [JobDisplayName("{0}")]
    public async Task ExecuteAsync(
        string jobId,
        string jobTypeName,
        PerformContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var console = new HangfireConsoleJobProgress(context);
        progressContext.Current = console;
        var startedAt = Stopwatch.GetTimestamp();
        console.WriteLine(ConsoleTextColor.Gray, $"▶ 开始 {jobId}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maxMinutes = options.Value.MaxJobRuntimeMinutes;
        if (maxMinutes > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(maxMinutes));
        }

        try
        {
            await executor.ExecuteAsync(jobId, jobTypeName, timeoutCts.Token);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            console.WriteLine(ConsoleTextColor.Green, $"✔ 完成 {jobId}（耗时 {FormatElapsed(elapsed)}）");
            logger.LogInformation("任务 {JobId} 完成，耗时 {ElapsedMs} ms。", jobId, elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            console.WriteLine(ConsoleTextColor.Red, $"✘ 失败 {jobId}（耗时 {FormatElapsed(elapsed)}）：{ex.GetType().Name}: {ex.Message}");
            logger.LogError(ex, "任务 {JobId} 失败，耗时 {ElapsedMs} ms。", jobId, elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            progressContext.Current = NullJobProgress.Instance;
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalMilliseconds < 1000
            ? $"{elapsed.TotalMilliseconds:F0} ms"
            : $"{elapsed.TotalSeconds:F1} s";
}

internal sealed class HangfireConsoleJobProgress(PerformContext context) : IJobProgress
{
    private readonly object _gate = new();
    private IProgressBar? _bar;

    public void WriteLine(string message)
    {
        lock (_gate)
        {
            context.WriteLine(message);
        }
    }

    public void WriteLine(ConsoleTextColor color, string message)
    {
        lock (_gate)
        {
            context.WriteLine(color, message);
        }
    }

    public void SetProgress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            _bar ??= context.WriteProgressBar("进度", 0);
            _bar.SetValue(percent);
        }
    }
}
