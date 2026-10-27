using System.Diagnostics;
using Hangfire;
using Hangfire.Console;
using Hangfire.Console.Progress;
using Hangfire.Server;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

/// <summary>
/// Hangfire 入口：必须带 PerformContext，Console 才能写入作业详情页。
/// </summary>
public sealed class HangfireJobGateway(JobExecutor executor, JobProgressContext progressContext)
{
    [JobDisplayName("{0}")]
    public async Task ExecuteAsync(
        string jobId,
        string jobTypeName,
        PerformContext context,
        IJobCancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cancellation = token?.ShutdownToken ?? CancellationToken.None;
        var console = new HangfireConsoleJobProgress(context);
        progressContext.Current = console;
        var startedAt = Stopwatch.GetTimestamp();
        console.WriteLine(ConsoleTextColor.Gray, $"▶ 开始 {jobId}");

        try
        {
            await executor.ExecuteAsync(jobId, jobTypeName, cancellation);
            console.WriteLine(ConsoleTextColor.Green, $"✔ 完成 {jobId}（耗时 {FormatElapsed(startedAt)}）");
        }
        catch (Exception ex)
        {
            console.WriteLine(ConsoleTextColor.Red, $"✘ 失败 {jobId}（耗时 {FormatElapsed(startedAt)}）：{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            progressContext.Current = NullJobProgress.Instance;
        }
    }

    private static string FormatElapsed(long startedAt)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        return elapsed.TotalMilliseconds < 1000
            ? $"{elapsed.TotalMilliseconds:F0} ms"
            : $"{elapsed.TotalSeconds:F1} s";
    }
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
