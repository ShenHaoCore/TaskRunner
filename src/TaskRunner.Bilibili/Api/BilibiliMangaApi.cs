using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// 
/// </summary>
public interface IBilibiliMangaApi
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BilibiliApiResponse> ClockInAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 
/// </summary>
/// <param name="http"></param>
internal sealed class BilibiliMangaApi(BilibiliHttpClient http) : IBilibiliMangaApi
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse> ClockInAsync(CancellationToken cancellationToken)
        => http.PostJsonAsync(BilibiliEndpoints.MangaClockIn, "{}", cancellationToken, referer: BilibiliEndpoints.MangaHome, origin: BilibiliEndpoints.MangaOrigin);
}
