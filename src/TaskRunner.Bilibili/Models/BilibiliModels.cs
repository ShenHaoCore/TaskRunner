using System.Text.Json.Serialization;

namespace TaskRunner.Bilibili.Models;

public class BilibiliApiResponse
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

public class BilibiliApiResponse<T> : BilibiliApiResponse
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

    /// <summary>会员状态：1 有效。</summary>
    [JsonPropertyName("vipStatus")]
    public int VipStatus { get; set; }

    /// <summary>会员类型：0 无 / 1 月度 / 2 年度。</summary>
    [JsonPropertyName("vipType")]
    public int VipType { get; set; }

    [JsonPropertyName("wallet")]
    public NavWallet? Wallet { get; set; }

    [JsonPropertyName("wbi_img")]
    public WbiImg? WbiImg { get; set; }

    public bool IsAnnualVip => VipStatus is 1 && VipType is 2;
}

public sealed class NavWallet
{
    /// <summary>B 币券余额（大会员每月赠送）。</summary>
    [JsonPropertyName("coupon_balance")]
    public decimal CouponBalance { get; set; }
}

/// <summary>wbi 签名所需的 img/sub 钥匙（nav.wbi_img 中图片 URL 的文件名）。</summary>
public sealed class WbiImg
{
    [JsonPropertyName("img_url")]
    public string? ImgUrl { get; set; }

    [JsonPropertyName("sub_url")]
    public string? SubUrl { get; set; }
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

/// <summary>稿件详情（/x/web-interface/view），用于补 cid。</summary>
public sealed class VideoViewData
{
    [JsonPropertyName("cid")]
    public long Cid { get; set; }
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

    /// <summary>空间投稿列表不返回 cid，心跳前需通过 view 接口补取。</summary>
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

/// <summary>充电 V2 结果：status=4 表示支付成功。</summary>
public sealed class ChargeV2Data
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("order_no")]
    public string? OrderNo { get; set; }
}
