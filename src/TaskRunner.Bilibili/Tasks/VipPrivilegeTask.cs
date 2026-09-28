using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>大会员权益任务：每日幂等领取 B 币券与漫画福利券（非年度大会员静默跳过）。</summary>
public sealed class VipPrivilegeTask(
    IBilibiliVipApi vip,
    IOptions<BilibiliOptions> options,
    ILogger<VipPrivilegeTask> logger) : BilibiliTaskBase(logger), IVipPrivilegeTask
{
    public async Task ExecuteAsync(IJobProgress progress, NavData nav, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableVipPrivilege || !nav.IsAnnualVip)
        {
            return;
        }

        var list = await GuardAsync<VipPrivilegeList>("大会员权益",
            () => vip.GetPrivilegesAsync(cancellationToken), cancellationToken);
        if (!list.IsSuccess || list.Data?.List is null)
        {
            logger.LogInformation("大会员权益查询跳过：code={Code} {Message}", list.Code, list.DisplayMessage);
            return;
        }

        foreach (var item in list.Data.List.Where(p => p.Type is 1 or 2 && p.State == 0))
        {
            var label = item.Type == 1 ? "B币券" : "漫画福利券";
            progress.WriteLine($"=== 每月阶段：领取{label} ===");
            var receive = await GuardAsync($"领取{label}",
                () => vip.ReceivePrivilegeAsync(item.Type, cancellationToken), cancellationToken);
            LogStep(progress, $"领取{label}", receive);
        }
    }
}
