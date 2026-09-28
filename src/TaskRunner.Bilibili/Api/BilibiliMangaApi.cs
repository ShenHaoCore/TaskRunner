using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>漫画域：每日签到。</summary>
public interface IBilibiliMangaApi
{
    Task<BilibiliApiResponse> ClockInAsync(CancellationToken cancellationToken);
}

internal sealed class BilibiliMangaApi(BilibiliHttpClient http) : IBilibiliMangaApi
{
    public Task<BilibiliApiResponse> ClockInAsync(CancellationToken cancellationToken)
        // 空 JSON 体比无 Content-Type 更稳妥；platform=android 在端点 query 中。
        => http.PostJsonAsync(
            BilibiliEndpoints.MangaClockIn,
            "{}",
            cancellationToken,
            referer: BilibiliEndpoints.MangaHome,
            origin: BilibiliEndpoints.MangaOrigin);
}
