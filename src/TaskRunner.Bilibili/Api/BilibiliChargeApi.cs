using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// 
/// </summary>
public interface IBilibiliChargeApi
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="upMid"></param>
    /// <param name="bpNum"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BilibiliApiResponse<ChargeV2Data>> ChargeQuickAsync(long upMid, int bpNum, CancellationToken cancellationToken);
}

/// <summary>
/// 
/// </summary>
/// <param name="http"></param>
internal sealed class BilibiliChargeApi(BilibiliHttpClient http) : IBilibiliChargeApi
{
    public Task<BilibiliApiResponse<ChargeV2Data>> ChargeQuickAsync(long upMid, int bpNum, CancellationToken cancellationToken)
        => http.PostFormAsync<ChargeV2Data>(BilibiliEndpoints.ChargeQuickV2, new Dictionary<string, string?>
        {
            ["bp_num"] = bpNum.ToString(),
            ["up_mid"] = upMid.ToString(),
            ["otype"] = "up",
            ["oid"] = upMid.ToString(),
            ["csrf"] = http.Cookie.BilibiliJct
        }, cancellationToken, referer: BilibiliEndpoints.WwwHome, origin: BilibiliEndpoints.WwwOrigin);
}
