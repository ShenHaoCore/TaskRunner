using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>直播域：签到、银瓜子钱包与兑换。</summary>
public interface IBilibiliLiveApi
{
    Task<BilibiliApiResponse> SignAsync(CancellationToken cancellationToken);

    Task<BilibiliApiResponse<LiveWalletStatus>> GetWalletStatusAsync(CancellationToken cancellationToken);

    Task<BilibiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken);
}

internal sealed class BilibiliLiveApi(BilibiliHttpClient http) : IBilibiliLiveApi
{
    public Task<BilibiliApiResponse> SignAsync(CancellationToken cancellationToken)
        => http.GetAsync(
            BilibiliEndpoints.LiveSign,
            cancellationToken,
            referer: BilibiliEndpoints.LinkHome,
            origin: BilibiliEndpoints.LinkOrigin);

    public Task<BilibiliApiResponse<LiveWalletStatus>> GetWalletStatusAsync(CancellationToken cancellationToken)
        => http.GetJsonAsync<LiveWalletStatus>(
            BilibiliEndpoints.LiveWalletStatus,
            cancellationToken,
            referer: BilibiliEndpoints.LinkHome,
            origin: BilibiliEndpoints.LinkOrigin);

    public Task<BilibiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken)
        => http.PostFormAsync(
            BilibiliEndpoints.Silver2Coin,
            new Dictionary<string, string?> { ["csrf_token"] = http.Cookie.BilibiliJct, ["csrf"] = http.Cookie.BilibiliJct },
            cancellationToken,
            referer: BilibiliEndpoints.LinkHome,
            origin: BilibiliEndpoints.LinkOrigin);
}
