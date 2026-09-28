using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// 
/// </summary>
public interface IBilibiliLiveApi
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BilibiliApiResponse> SignAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BilibiliApiResponse<LiveWalletStatus>> GetWalletStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BilibiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 
/// </summary>
/// <param name="http"></param>
internal sealed class BilibiliLiveApi(BilibiliHttpClient http) : IBilibiliLiveApi
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse> SignAsync(CancellationToken cancellationToken)
        => http.GetAsync(BilibiliEndpoints.LiveSign, cancellationToken, referer: BilibiliEndpoints.LinkHome, origin: BilibiliEndpoints.LinkOrigin);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse<LiveWalletStatus>> GetWalletStatusAsync(CancellationToken cancellationToken)
        => http.GetJsonAsync<LiveWalletStatus>(BilibiliEndpoints.LiveWalletStatus, cancellationToken, referer: BilibiliEndpoints.LinkHome, origin: BilibiliEndpoints.LinkOrigin);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken)
        => http.PostFormAsync(BilibiliEndpoints.Silver2Coin, new Dictionary<string, string?> { ["csrf_token"] = http.Cookie.BilibiliJct, ["csrf"] = http.Cookie.BilibiliJct }, cancellationToken, referer: BilibiliEndpoints.LinkHome, origin: BilibiliEndpoints.LinkOrigin);
}
