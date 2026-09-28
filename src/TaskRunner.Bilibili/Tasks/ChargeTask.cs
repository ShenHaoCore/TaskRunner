using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>
/// 充电任务
/// </summary>
/// <param name="charge">Bilibili 充电 API</param>
/// <param name="options">Bilibili 配置选项</param>
/// <param name="logger">日志记录器</param>
public sealed class ChargeTask(
    IBilibiliChargeApi charge,
    IOptions<BilibiliOptions> options,
    ILogger<ChargeTask> logger) : BilibiliTaskBase(logger), IChargeTask
{
    // 未配置充电对象时的兜底 UP（B 站已禁止给自己充电），与主流脚本一致指向官方号。
    private const long FallbackChargeUpMid = 2L;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="progress"></param>
    /// <param name="nav"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task ExecuteAsync(IJobProgress progress, NavData nav, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableCharge || !nav.IsAnnualVip) { return; }
        var coupon = (int)(nav.Wallet?.CouponBalance ?? 0);
        if (coupon < 2) { return; }

        var upMid = options.Value.ChargeUpMid > 0 ? options.Value.ChargeUpMid : FallbackChargeUpMid;
        var result = await GuardAsync<ChargeV2Data>("B币券充电", () => charge.ChargeQuickAsync(upMid, coupon, cancellationToken), cancellationToken);
        if (result.IsSuccess && result.Data?.Status is 4) { progress.WriteLine($"[B币券充电] 同步完成（{coupon} 电池 → UP {upMid}，订单 {result.Data.OrderNo}）"); }
        else
        {
            progress.WriteLine($"[B币券充电] 失败 code={result.Code} {result.DisplayMessage}");
            logger.LogWarning("B币券充电失败：code={Code} {Message}", result.Code, result.DisplayMessage);
        }
    }
}
