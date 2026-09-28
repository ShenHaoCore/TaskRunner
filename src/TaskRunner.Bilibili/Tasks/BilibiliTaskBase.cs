using Microsoft.Extensions.Logging;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>B 站单个任务的公共基类：提供网络异常收敛（Guard）与统一日志（LogStep）。</summary>
public abstract class BilibiliTaskBase(ILogger logger)
{
    // 常见「已完成」类错误码，不中断管线（65006 已赞过）
    private static readonly int[] SkippableCodes = [71000, 1011040, 34005, -104, 65006];

    /// <summary>把单步网络异常收敛为业务响应，避免一个节点故障中断整条管线。</summary>
    protected async Task<BilibiliApiResponse> GuardAsync(
        string sector, Func<Task<BilibiliApiResponse>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Sector} 网络异常，已跳过。", sector);
            return new BilibiliApiResponse { Code = -1, Message = $"网络异常：{ex.Message}" };
        }
    }

    /// <summary>带数据响应的单步网络异常收敛。</summary>
    protected async Task<BilibiliApiResponse<TData>> GuardAsync<TData>(
        string sector, Func<Task<BilibiliApiResponse<TData>>> action, CancellationToken cancellationToken)
        where TData : class
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Sector} 网络异常，已跳过。", sector);
            return new BilibiliApiResponse<TData> { Code = -1, Message = $"网络异常：{ex.Message}" };
        }
    }

    protected void LogStep(IJobProgress progress, string sector, BilibiliApiResponse response)
    {
        if (response.IsSuccess)
        {
            progress.WriteLine($"[{sector}] 同步完成");
            return;
        }

        if (SkippableCodes.Contains(response.Code))
        {
            progress.WriteLine($"[{sector}] 跳过 code={response.Code} {response.DisplayMessage}");
            logger.LogInformation("{Sector} 已跳过：{Code} {Message}", sector, response.Code, response.DisplayMessage);
            return;
        }

        progress.WriteLine($"[{sector}] 失败 code={response.Code} {response.DisplayMessage}");
        logger.LogWarning("{Sector} 失败：{Code} {Message}", sector, response.Code, response.DisplayMessage);
    }
}
