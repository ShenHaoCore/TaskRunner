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
