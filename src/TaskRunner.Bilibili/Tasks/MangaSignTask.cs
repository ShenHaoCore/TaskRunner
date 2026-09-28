using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>漫画签到任务：支持重复签到（code=1/-1）幂等处理。</summary>
public sealed class MangaSignTask(
    IBilibiliMangaApi manga,
    IOptions<BilibiliOptions> options,
    ILogger<MangaSignTask> logger) : BilibiliTaskBase(logger), IMangaSignTask
{
    public async Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableMangaSign)
        {
            progress.WriteLine("[漫画节点] 离线（配置已关闭）");
            return;
        }

        var result = await GuardAsync("漫画节点", () => manga.ClockInAsync(cancellationToken), cancellationToken);
        // 重复签到常见 code=1 / -1
        if (result.IsSuccess || result.Code is 1 or -1)
        {
            progress.WriteLine($"[漫画节点] 跳过 code={result.Code} {result.DisplayMessage}");
        }
        else
        {
            logger.LogWarning("漫画签到失败：code={Code} {Message}", result.Code, result.DisplayMessage);
            progress.WriteLine($"[漫画节点] 失败 code={result.Code} {result.DisplayMessage}");
        }
    }
}
