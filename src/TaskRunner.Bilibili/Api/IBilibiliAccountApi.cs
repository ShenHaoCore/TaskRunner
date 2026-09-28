using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>账号域：登录态、设备指纹、关注关系。</summary>
public interface IBilibiliAccountApi
{
    BilibiliCookie Cookie { get; }

    Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken);

    /// <summary>导航接口校验登录态，成功后写入 wbi 钥匙与钱包信息。</summary>
    Task<BilibiliApiResponse<NavData>> GetNavAsync(CancellationToken cancellationToken);

    /// <summary>获取本人关注列表（按关注时间倒序）。</summary>
    Task<BilibiliApiResponse<FollowingListData>> GetFollowingsAsync(long selfMid, int pageSize, CancellationToken cancellationToken);

    /// <summary>读取今日经验任务完成状态。</summary>
    Task<BilibiliApiResponse<DailyTaskInfo>> GetDailyTaskAsync(CancellationToken cancellationToken);
}
