using System.Text.Json.Serialization;

namespace TaskRunner.Bilibili.Models;

public class BiliApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    public string DisplayMessage => Message ?? Msg ?? string.Empty;

    public bool IsSuccess => Code is 0;
}

public class BiliApiResponse<T> : BiliApiResponse
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class NavData
{
    [JsonPropertyName("isLogin")]
    public bool IsLogin { get; set; }

    [JsonPropertyName("mid")]
    public long Mid { get; set; }

    [JsonPropertyName("uname")]
    public string? Uname { get; set; }

    [JsonPropertyName("money")]
    public decimal? Money { get; set; }
}

public sealed class DailyTaskInfo
{
    [JsonPropertyName("login")]
    public bool Login { get; set; }

    [JsonPropertyName("watch")]
    public bool Watch { get; set; }

    [JsonPropertyName("share")]
    public bool Share { get; set; }

    [JsonPropertyName("coins")]
    public long Coins { get; set; }
}

public sealed class PopularListData
{
    [JsonPropertyName("list")]
    public List<PopularVideo>? List { get; set; }
}

public sealed class PopularVideo
{
    [JsonPropertyName("aid")]
    public long Aid { get; set; }

    [JsonPropertyName("bvid")]
    public string? Bvid { get; set; }

    [JsonPropertyName("cid")]
    public long Cid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }
}

public sealed class FollowingInfo
{
    [JsonPropertyName("mid")]
    public long Mid { get; set; }

    [JsonPropertyName("uname")]
    public string? Uname { get; set; }
}

public sealed class FollowingListData
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("list")]
    public List<FollowingInfo>? List { get; set; }
}

public sealed class UpVideoInfo
{
    [JsonPropertyName("aid")]
    public long Aid { get; set; }

    [JsonPropertyName("bvid")]
    public string? Bvid { get; set; }

    [JsonPropertyName("cid")]
    public long Cid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }
}

public sealed class UpVideoPage
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public sealed class UpVideoList
{
    [JsonPropertyName("vlist")]
    public List<UpVideoInfo>? Vlist { get; set; }
}

public sealed class UpVideoListData
{
    [JsonPropertyName("page")]
    public UpVideoPage? Page { get; set; }

    [JsonPropertyName("list")]
    public UpVideoList? List { get; set; }
}

public sealed class LiveWalletStatus
{
    [JsonPropertyName("silver")]
    public long Silver { get; set; }

    [JsonPropertyName("gold")]
    public long Gold { get; set; }

    [JsonPropertyName("coin")]
    public decimal Coin { get; set; }

    [JsonPropertyName("silver_2_coin_left")]
    public int Silver2CoinLeft { get; set; }
}

/// <summary>大会员权益项（type=1 B 币券，type=2 漫画福利券；state=0 未领取）。</summary>
public sealed class VipPrivilegeInfo
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("state")]
    public int State { get; set; }

    [JsonPropertyName("expire_time")]
    public long ExpireTime { get; set; }
}

public sealed class VipPrivilegeList
{
    [JsonPropertyName("list")]
    public List<VipPrivilegeInfo>? List { get; set; }
}

public sealed class ChargeWalletData
{
    [JsonPropertyName("wallet")]
    public BcoinWallet? Wallet { get; set; }

    /// <summary>B 币券余额。</summary>
    [JsonPropertyName("coupon_balance")]
    public decimal CouponBalance { get; set; }
}

public sealed class BcoinWallet
{
    [JsonPropertyName("bcoin_balance")]
    public decimal BcoinBalance { get; set; }
}
