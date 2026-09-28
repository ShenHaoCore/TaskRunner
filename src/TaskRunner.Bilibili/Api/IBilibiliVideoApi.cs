using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>视频域：选源、播放上报、分享、点赞、投币。</summary>
public interface IBilibiliVideoApi
{
    Task<BilibiliApiResponse<PopularListData>> GetPopularAsync(int pageSize, CancellationToken cancellationToken);

    Task<BilibiliApiResponse<VideoViewData>> GetVideoViewAsync(string bvid, long aid, CancellationToken cancellationToken);

    Task<BilibiliApiResponse> HeartbeatAsync(long aid, long cid, string bvid, long mid, int playedTime, CancellationToken cancellationToken);

    Task<BilibiliApiResponse> ShareVideoAsync(long aid, string bvid, CancellationToken cancellationToken);

    Task<BilibiliApiResponse> LikeAsync(long aid, string bvid, CancellationToken cancellationToken);

    Task<BilibiliApiResponse> AddCoinAsync(long aid, string bvid, int multiply, CancellationToken cancellationToken);

    /// <summary>查询 UP 主投稿列表（wbi 签名接口）。</summary>
    Task<BilibiliApiResponse<UpVideoListData>> GetUpVideosAsync(long upMid, int pageSize, int page, CancellationToken cancellationToken);
}
