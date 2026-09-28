using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>银瓜子兑换任务：查询钱包状态后按条件兑换硬币。</summary>
public sealed class SilverTask(
    IBilibiliLiveApi live,
    IOptions<BilibiliOptions> options,
    ILogger<SilverTask> logger) : BilibiliTaskBase(logger), ISilverTask
{
    public async Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableSilver2Coin)
        {
            progress.WriteLine("[银瓜子兑换] 离线（配置已关闭）");
            return;
        }

        var wallet = await GuardAsync<LiveWalletStatus>("钱包扫描",
            () => live.GetWalletStatusAsync(cancellationToken), cancellationToken);
        if (!wallet.IsSuccess || wallet.Data is null)
        {
            progress.WriteLine($"[钱包扫描] 读取失败 code={wallet.Code} {wallet.DisplayMessage}");
            return;
        }

        progress.WriteLine($"[钱包扫描] 银瓜子={wallet.Data.Silver} | 今日可兑换={wallet.Data.Silver2CoinLeft}");
        if (wallet.Data.Silver2CoinLeft > 0 && wallet.Data.Silver >= 700)
        {
            var exchange = await GuardAsync("银瓜子兑换",
                () => live.Silver2CoinAsync(cancellationToken), cancellationToken);
            LogStep(progress, "银瓜子兑换", exchange);
        }
        else
        {
            progress.WriteLine("[银瓜子兑换] 跳过（银瓜子不足或今日已兑换）");
        }
    }
}
