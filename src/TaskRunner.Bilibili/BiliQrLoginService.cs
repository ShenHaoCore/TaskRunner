using Microsoft.Extensions.Logging;
using QRCoder;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili;

/// <summary>
/// 
/// </summary>
public enum BiliQrLoginStatus
{
    Waiting = 0,
    Scanned = 1,
    Success = 2,
    Expired = 3,
    Error = 4
}

/// <summary>
/// 
/// </summary>
/// <param name="QrcodeKey"></param>
/// <param name="Url"></param>
/// <param name="QrImageDataUrl"></param>
public sealed record BiliQrGenerateResult(string QrcodeKey, string Url, string QrImageDataUrl);

/// <summary>
/// 
/// </summary>
/// <param name="Status"></param>
/// <param name="Message"></param>
/// <param name="Cookie"></param>
/// <param name="Saved"></param>
public sealed record BiliQrPollResult(BiliQrLoginStatus Status, string? Message, string? Cookie, bool Saved);

/// <summary>
/// 
/// </summary>
public interface IBiliQrLoginService
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BiliQrGenerateResult> GenerateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="qrcodeKey"></param>
    /// <param name="saveOnSuccess"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<BiliQrPollResult> PollAsync(string qrcodeKey, bool saveOnSuccess, CancellationToken cancellationToken);
}

/// <summary>
/// 
/// </summary>
/// <param name="http"></param>
/// <param name="cookieStore"></param>
/// <param name="logger"></param>
public sealed class BiliQrLoginService(HttpClient http, IBilibiliCookieStore cookieStore, ILogger<BiliQrLoginService> logger) : IBiliQrLoginService
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<BiliQrGenerateResult> GenerateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BiliEndpoints.QrGenerate);
        request.Headers.TryAddWithoutValidation("Referer", BiliEndpoints.WwwHome);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<BiliApiResponse<QrCodeData>>(BiliJson.Options, cancellationToken) ?? throw new InvalidOperationException("生成二维码：空响应。");
        if (!payload.IsSuccess || payload.Data is null || string.IsNullOrWhiteSpace(payload.Data.QrcodeKey)) { throw new InvalidOperationException($"生成二维码失败：code={payload.Code} {payload.DisplayMessage}"); }

        var url = payload.Data.Url ?? throw new InvalidOperationException("生成二维码失败：缺少 url。");
        return new BiliQrGenerateResult(payload.Data.QrcodeKey, url, ToDataUrl(url));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="qrcodeKey"></param>
    /// <param name="saveOnSuccess"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<BiliQrPollResult> PollAsync(string qrcodeKey, bool saveOnSuccess, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrcodeKey)) { throw new ArgumentException("qrcodeKey 不能为空。", nameof(qrcodeKey)); }
        var url = $"{BiliEndpoints.QrPoll}?qrcode_key={Uri.EscapeDataString(qrcodeKey.Trim())}&source=main_mini";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", BiliEndpoints.WwwHome);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) { return new BiliQrPollResult(BiliQrLoginStatus.Error, $"HTTP {(int)response.StatusCode}", null, false); }

        var payload = await response.Content.ReadFromJsonAsync<BiliApiResponse<QrPollData>>(BiliJson.Options, cancellationToken);
        if (payload is null || !payload.IsSuccess || payload.Data is null) { return new BiliQrPollResult(BiliQrLoginStatus.Error, payload?.DisplayMessage ?? "检测接口异常", null, false); }

        return payload.Data.Code switch
        {
            0 => await OnSuccessAsync(response, saveOnSuccess, cancellationToken),
            86038 => new BiliQrPollResult(BiliQrLoginStatus.Expired, payload.Data.Message ?? "二维码已失效", null, false),
            86090 => new BiliQrPollResult(BiliQrLoginStatus.Scanned, payload.Data.Message ?? "已扫码，待确认", null, false),
            86101 => new BiliQrPollResult(BiliQrLoginStatus.Waiting, payload.Data.Message ?? "等待扫码", null, false),
            _ => new BiliQrPollResult(BiliQrLoginStatus.Waiting, payload.Data.Message ?? $"code={payload.Data.Code}", null, false)
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="response"></param>
    /// <param name="saveOnSuccess"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<BiliQrPollResult> OnSuccessAsync(HttpResponseMessage response, bool saveOnSuccess, CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            logger.LogWarning("扫码成功但响应未包含 Set-Cookie。");
            return new BiliQrPollResult(BiliQrLoginStatus.Error, "扫码成功但未返回 Cookie", null, false);
        }

        var cookie = BiliCookie.FromSetCookieHeaders(setCookies);
        var parsed = new BiliCookie(cookie);
        if (!parsed.HasLoginTokens) { return new BiliQrPollResult(BiliQrLoginStatus.Error, "扫码成功但 Cookie 缺少 SESSDATA/bili_jct", cookie, false); }

        var saved = false;
        if (saveOnSuccess)
        {
            var enriched = await EnrichWithBuvidAsync(cookie, cancellationToken);
            await cookieStore.SaveAsync(enriched, cancellationToken);
            cookie = enriched;
            saved = true;
        }

        logger.LogInformation("B 站扫码登录成功，Cookie 已{Action}。", saved ? "落盘" : "返回未保存");
        return new BiliQrPollResult(BiliQrLoginStatus.Success, "登录成功", cookie, saved);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cookie"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<string> EnrichWithBuvidAsync(string cookie, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BiliEndpoints.WwwHome);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("Referer", BiliEndpoints.WwwHome);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) { return cookie; }
            return new BiliCookie(cookie).MergeSetCookies(setCookies).Raw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "扫码后补齐 buvid 失败，将使用原始 Cookie。");
            return cookie;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    private static string ToDataUrl(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(8);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>
    /// 
    /// </summary>
    private sealed class QrCodeData
    {
        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("qrcode_key")]
        public string? QrcodeKey { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    private sealed class QrPollData
    {
        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
