using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>大会员域：每月权益（B 币券 / 漫画福利券）查询与领取。</summary>
public interface IBilibiliVipApi
{
    Task<BilibiliApiResponse<VipPrivilegeList>> GetPrivilegesAsync(CancellationToken cancellationToken);

    /// <summary>领取权益（幂等：仅对 state=0 的项调用）。参数必须走 query string。</summary>
    Task<BilibiliApiResponse> ReceivePrivilegeAsync(int type, CancellationToken cancellationToken);
}

internal sealed class BilibiliVipApi(BilibiliHttpClient http) : IBilibiliVipApi
{
    private const string BigOrigin = "https://big.bilibili.com";

    public Task<BilibiliApiResponse<VipPrivilegeList>> GetPrivilegesAsync(CancellationToken cancellationToken)
        => http.GetJsonAsync<VipPrivilegeList>(
            BilibiliEndpoints.VipPrivilegeList,
            cancellationToken,
            referer: BilibiliEndpoints.BigPointPage,
            origin: BigOrigin);

    public Task<BilibiliApiResponse> ReceivePrivilegeAsync(int type, CancellationToken cancellationToken)
        => http.PostEmptyAsync(
            BilibiliEndpoints.VipPrivilegeReceive,
            cancellationToken,
            query: new Dictionary<string, string?>
            {
                ["type"] = type.ToString(),
                ["csrf"] = http.Cookie.BilibiliJct
            },
            referer: BilibiliEndpoints.BigPointPage,
            origin: BigOrigin);
}
