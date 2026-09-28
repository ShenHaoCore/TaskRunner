using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QRCoder;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili;

public enum BilibiliQrLoginStatus
{
    Waiting = 0,
    Scanned = 1,
    Success = 2,
    Expired = 3,
    Error = 4
}

public sealed record BilibiliQrGenerateResult(string QrcodeKey, string Url, string QrImageDataUrl);

public sealed record BilibiliQrPollResult(BilibiliQrLoginStatus Status, string? Message, string? Cookie, bool Saved);

public interface IBilibiliQrLoginService
{
    Task<BilibiliQrGenerateResult> GenerateAsync(CancellationToken cancellationToken);

    Task<BilibiliQrPollResult> PollAsync(string qrcodeKey, bool saveOnSuccess, CancellationToken cancellationToken);
}

public sealed class BilibiliQrLoginService(
    BilibiliHttpClient http,
    IBilibiliCookieStore cookieStore,
    ILogger<BilibiliQrLoginService> logger) : IBilibiliQrLoginService
{
    public async Task<BilibiliQrGenerateResult> GenerateAsync(CancellationToken cancellationToken)
    {
        using var request = http.CreateRequest(HttpMethod.Get, BilibiliEndpoints.QrGenerate, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwOrigin);
        using var response = await http.SendRawAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<BilibiliApiResponse<QrCodeData>>(BilibiliJsonDefaults.Options, cancellationToken)
            ?? throw new InvalidOperationException("生成二维码：空响应。");
        if (!payload.IsSuccess || payload.Data is null || string.IsNullOrWhiteSpace(payload.Data.QrcodeKey))
        {
            throw new InvalidOperationException($"生成二维码失败：code={payload.Code} {payload.DisplayMessage}");
        }

        var url = payload.Data.Url ?? throw new InvalidOperationException("生成二维码失败：缺少 url。");
        return new BilibiliQrGenerateResult(payload.Data.QrcodeKey, url, ToDataUrl(url));
    }

    public async Task<BilibiliQrPollResult> PollAsync(string qrcodeKey, bool saveOnSuccess, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrcodeKey))
        {
            throw new ArgumentException("qrcodeKey 不能为空。", nameof(qrcodeKey));
        }

        var url = $"{BilibiliEndpoints.QrPoll}?qrcode_key={Uri.EscapeDataString(qrcodeKey.Trim())}&source=main_mini";
        using var request = http.CreateRequest(HttpMethod.Get, url, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwOrigin);
        using var response = await http.SendRawAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new BilibiliQrPollResult(BilibiliQrLoginStatus.Error, $"HTTP {(int)response.StatusCode}", null, false);
        }

        var payload = await response.Content.ReadFromJsonAsync<BilibiliApiResponse<QrPollData>>(BilibiliJsonDefaults.Options, cancellationToken);
        if (payload is null || !payload.IsSuccess || payload.Data is null)
        {
            return new BilibiliQrPollResult(BilibiliQrLoginStatus.Error, payload?.DisplayMessage ?? "检测接口异常", null, false);
        }

        return payload.Data.Code switch
        {
            0 => await OnSuccessAsync(response, saveOnSuccess, cancellationToken),
            86038 => new BilibiliQrPollResult(BilibiliQrLoginStatus.Expired, payload.Data.Message ?? "二维码已失效", null, false),
            86090 => new BilibiliQrPollResult(BilibiliQrLoginStatus.Scanned, payload.Data.Message ?? "已扫码，待确认", null, false),
            86101 => new BilibiliQrPollResult(BilibiliQrLoginStatus.Waiting, payload.Data.Message ?? "等待扫码", null, false),
            _ => new BilibiliQrPollResult(BilibiliQrLoginStatus.Waiting, payload.Data.Message ?? $"code={payload.Data.Code}", null, false)
        };
    }

    private async Task<BilibiliQrPollResult> OnSuccessAsync(HttpResponseMessage response, bool saveOnSuccess, CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            logger.LogWarning("扫码成功但响应未包含 Set-Cookie。");
            return new BilibiliQrPollResult(BilibiliQrLoginStatus.Error, "扫码成功但未返回 Cookie", null, false);
        }

        var cookie = BilibiliCookie.FromSetCookieHeaders(setCookies);
        var parsed = new BilibiliCookie(cookie);
        if (!parsed.IsAuthenticated)
        {
            return new BilibiliQrPollResult(BilibiliQrLoginStatus.Error, "扫码成功但 Cookie 缺少 SESSDATA/bili_jct", cookie, false);
        }

        var saved = false;
        if (saveOnSuccess)
        {
            var enriched = await EnrichWithBuvidAsync(cookie, cancellationToken);
            await cookieStore.SaveAsync(enriched, cancellationToken);
            cookie = enriched;
            saved = true;
        }

        logger.LogInformation("B 站扫码登录成功，Cookie 已{Action}。", saved ? "落盘" : "返回未保存");
        return new BilibiliQrPollResult(BilibiliQrLoginStatus.Success, "登录成功", cookie, saved);
    }

    private async Task<string> EnrichWithBuvidAsync(string cookie, CancellationToken cancellationToken)
    {
        try
        {
            using var request = http.CreateRequest(HttpMethod.Get, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwOrigin);
            // 用新登录的 Cookie 替换默认 Cookie
            request.Headers.Remove("Cookie");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var response = await http.SendRawAsync(request, cancellationToken);
            if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                return cookie;
            }

            return new BilibiliCookie(cookie).MergeSetCookies(setCookies).Raw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "扫码后补齐 buvid 失败，将使用原始 Cookie。");
            return cookie;
        }
    }

    private static string ToDataUrl(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(8);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private sealed class QrCodeData
    {
        [JsonPropertyName("qrcode_key")]
        public string? QrcodeKey { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class QrPollData
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
