using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>充电域：月底用 B 币券余额给 UP 主充电（status=4 为支付成功）。</summary>
public interface IBilibiliChargeApi
{
    Task<BilibiliApiResponse<ChargeV2Data>> ChargeQuickAsync(long upMid, int bpNum, CancellationToken cancellationToken);
}

internal sealed class BilibiliChargeApi(BilibiliHttpClient http) : IBilibiliChargeApi
{
    public Task<BilibiliApiResponse<ChargeV2Data>> ChargeQuickAsync(long upMid, int bpNum, CancellationToken cancellationToken)
        => http.PostFormAsync<ChargeV2Data>(
            BilibiliEndpoints.ChargeQuickV2,
            new Dictionary<string, string?>
            {
                ["bp_num"] = bpNum.ToString(),
                ["up_mid"] = upMid.ToString(),
                ["otype"] = "up",
                ["oid"] = upMid.ToString(),
                ["csrf"] = http.Cookie.BilibiliJct
            },
            cancellationToken,
            referer: BilibiliEndpoints.WwwHome,
            origin: BilibiliEndpoints.WwwOrigin);
}
