using Microsoft.Extensions.Logging;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili;

/// <summary>
/// 视频选取：优先从关注 UP 主的投稿中随机选择，失败/无关注时回退热门榜单。
/// 关注列表每次执行只拉取一次（wbi 空间接口有频控），选取全程吞掉网络异常以保证兜底可用。
/// </summary>
public interface IBilibiliVideoPicker
{
    Task<VideoTarget> PickAsync(long selfMid, CancellationToken cancellationToken);
}

internal sealed class BilibiliVideoPicker(
    IBilibiliAccountApi accountApi,
    IBilibiliVideoApi videoApi,
    ILogger<BilibiliVideoPicker> logger) : IBilibiliVideoPicker
{
    private const int FollowingPageSize = 50;
    private const int UpVideoPageSize = 30;
    private const int PopularPageSize = 20;
    private const int MaxUpAttempts = 3;
    private const int MinVideoSeconds = 20;

    private List<FollowingInfo>? _followingsCache;
    private bool _followingsLoaded;

    public async Task<VideoTarget> PickAsync(long selfMid, CancellationToken cancellationToken)
    {
        var fromFollowing = await TryPickFromFollowingAsync(selfMid, cancellationToken);
        if (fromFollowing is not null)
        {
            return fromFollowing;
        }

        return await PickFromPopularAsync(cancellationToken);
    }

    private async Task<VideoTarget?> TryPickFromFollowingAsync(long selfMid, CancellationToken cancellationToken)
    {
        var ups = await GetFollowingsAsync(selfMid, cancellationToken);
        if (ups is not { Count: > 0 })
        {
            return null;
        }

        for (var attempt = 0; attempt < MaxUpAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var up = ups[Random.Shared.Next(ups.Count)];
            try
            {
                var videos = await videoApi.GetUpVideosAsync(up.Mid, UpVideoPageSize, page: 1, cancellationToken);
                if (!videos.IsSuccess || videos.Data?.List?.Vlist is not { Count: > 0 } vlist)
                {
                    // -799 请求频繁/-403 风控：退避后再试下一个 UP。
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                var candidates = vlist.Where(IsDurationValid).ToList();
                if (candidates.Count is 0)
                {
                    continue;
                }

                var video = candidates[Random.Shared.Next(candidates.Count)];
                return new VideoTarget(
                    video.Aid,
                    video.Bvid ?? string.Empty,
                    video.Cid,
                    video.Title ?? string.Empty,
                    $"关注:{BilibiliNameMask.Mask(up.Uname)}");
            }
            catch (HttpRequestException ex)
            {
                logger.LogInformation(ex, "查询 UP 主 {UpMid} 投稿网络异常，尝试下一个。", up.Mid);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(ex, "查询 UP 主 {UpMid} 投稿超时，尝试下一个。", up.Mid);
            }
        }

        logger.LogInformation("关注的 UP 主暂无可用稿件，回退热门榜单。");
        return null;
    }

    private async Task<List<FollowingInfo>?> GetFollowingsAsync(long selfMid, CancellationToken cancellationToken)
    {
        if (_followingsLoaded)
        {
            return _followingsCache;
        }

        _followingsLoaded = true;
        try
        {
            var response = await accountApi.GetFollowingsAsync(selfMid, FollowingPageSize, cancellationToken);
            _followingsCache = response.IsSuccess ? response.Data?.List : null;
            if (_followingsCache is not { Count: > 0 })
            {
                logger.LogInformation("关注列表为空或查询失败 code={Code}，回退热门榜单。", response.Code);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "关注列表查询网络异常，回退热门榜单。");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "关注列表查询超时，回退热门榜单。");
        }

        return _followingsCache;
    }

    private async Task<VideoTarget> PickFromPopularAsync(CancellationToken cancellationToken)
    {
        var popular = await videoApi.GetPopularAsync(PopularPageSize, cancellationToken);
        if (!popular.IsSuccess)
        {
            throw new InvalidOperationException($"获取热门视频失败：code={popular.Code} {popular.DisplayMessage}");
        }

        var list = popular.Data?.List?
            .Where(item => item.Aid > 0 && !string.IsNullOrWhiteSpace(item.Bvid) && IsDurationValid(item))
            .ToList();
        if (list is not { Count: > 0 })
        {
            throw new InvalidOperationException("热门列表为空或无可用视频，无法选取。");
        }

        var video = list[Random.Shared.Next(list.Count)];
        return new VideoTarget(video.Aid, video.Bvid!, video.Cid, video.Title ?? string.Empty, "热门");
    }

    /// <summary>过滤短视频：心跳要上报 15 秒播放，时长未知的放行。</summary>
    private static bool IsDurationValid(UpVideoInfo video) => video.Duration is null or >= MinVideoSeconds;

    private static bool IsDurationValid(PopularVideo video) => video.Duration is null or >= MinVideoSeconds;
}
