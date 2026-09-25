using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Api.Auth;
using TaskRunner.Bilibili;

namespace TaskRunner.Api.Controllers;

[ApiController]
[Route("api/bilibili")]
[Authorize(Policy = AdminAuthDefaults.Policy)]
[Tags("哔哩哔哩")]
public sealed class BilibiliController(
    IBiliQrLoginService qrLogin,
    IBilibiliCookieStore cookieStore,
    BiliApiClient apiClient) : ControllerBase
{
    [HttpGet("account", Name = "GetBilibiliAccount")]
    [EndpointSummary("账号状态")]
    public async Task<ActionResult<object>> Account(CancellationToken cancellationToken)
    {
        var cookie = new BiliCookie(cookieStore.GetCookie());
        if (!cookie.HasLoginTokens)
        {
            return Ok(new
            {
                configured = false,
                cookieFile = cookieStore.FilePath,
                message = "未配置 Cookie，请扫码登录或写入配置。"
            });
        }

        try
        {
            var nav = await apiClient.GetNavAsync(cancellationToken);
            if (!nav.IsSuccess || nav.Data is not { IsLogin: true })
            {
                return Ok(new
                {
                    configured = true,
                    valid = false,
                    dedeUserId = cookie.DedeUserId,
                    message = nav.DisplayMessage
                });
            }

            return Ok(new
            {
                configured = true,
                valid = true,
                mid = nav.Data.Mid,
                uname = BiliNameMask.Mask(nav.Data.Uname),
                money = nav.Data.Money,
                dedeUserId = cookie.DedeUserId
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                configured = true,
                valid = false,
                dedeUserId = cookie.DedeUserId,
                message = ex.Message
            });
        }
    }

    [HttpPost("login/qrcode", Name = "CreateBilibiliQrLogin")]
    [EndpointSummary("生成扫码登录二维码")]
    public async Task<ActionResult<object>> CreateQr(CancellationToken cancellationToken)
    {
        var result = await qrLogin.GenerateAsync(cancellationToken);
        return Ok(new
        {
            qrcodeKey = result.QrcodeKey,
            url = result.Url,
            qrImageDataUrl = result.QrImageDataUrl,
            poll = $"/api/bilibili/login/qrcode/{Uri.EscapeDataString(result.QrcodeKey)}"
        });
    }

    [HttpGet("login/qrcode/{qrcodeKey}", Name = "PollBilibiliQrLogin")]
    [EndpointSummary("轮询扫码状态")]
    [EndpointDescription("成功时写入 Cookie 文件，Api/Worker 同机共享。")]
    public async Task<ActionResult<object>> Poll(
        string qrcodeKey,
        [FromQuery] bool save = true,
        CancellationToken cancellationToken = default)
    {
        var result = await qrLogin.PollAsync(qrcodeKey, save, cancellationToken);
        return Ok(new
        {
            status = result.Status.ToString(),
            message = result.Message,
            saved = result.Saved,
            hasCookie = !string.IsNullOrWhiteSpace(result.Cookie),
            cookieFile = result.Saved ? cookieStore.FilePath : null
        });
    }

    [HttpDelete("cookie", Name = "ClearBilibiliCookie")]
    [EndpointSummary("清除扫码落盘 Cookie")]
    public async Task<ActionResult<object>> ClearCookie(CancellationToken cancellationToken)
    {
        await cookieStore.ClearAsync(cancellationToken);
        return Ok(new { cleared = true, path = cookieStore.FilePath });
    }
}
