using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("认证")]
public sealed class AuthController(IOptions<TaskRunnerOptions> options, IHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login", Name = "AdminLogin")]
    [EndpointSummary("登录")]
    [EndpointDescription("写入管理员 Cookie。")]
    public ActionResult Login(AdminLoginRequest request)
    {
        if (environment.IsDevelopment())
        {
            Response.Cookies.Append(AdminAuthDefaults.CookieName, "development", CreateCookieOptions());
            return Ok(new { message = "Development 环境已写入 Cookie。" });
        }

        var key = options.Value.AdminApiKey;
        if (string.IsNullOrEmpty(key) || !string.Equals(request.ApiKey, key, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "ApiKey 无效。" });
        }

        Response.Cookies.Append(AdminAuthDefaults.CookieName, key, CreateCookieOptions());
        return Ok(new { message = "已登录。可打开 Dashboard。" });
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("logout", Name = "AdminLogout")]
    [EndpointSummary("退出")]
    public ActionResult Logout()
    {
        Response.Cookies.Delete(AdminAuthDefaults.CookieName);
        return Ok(new { message = "已退出。" });
    }

    private static CookieOptions CreateCookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = false,
        Path = "/"
    };
}
