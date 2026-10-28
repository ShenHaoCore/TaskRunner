using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Contracts;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("认证")]
public sealed class AuthController(IOptions<TaskRunnerOptions> options, IHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(AdminAuthDefaults.LoginRateLimitPolicy)]
    [HttpPost("login", Name = "AdminLogin")]
    [EndpointSummary("登录")]
    [EndpointDescription("校验 ApiKey 后签发 Cookie 会话票据（非明文密钥）。生产环境按 IP 限流（5 次/分钟，超出 429）。")]
    public async Task<ActionResult> Login(AdminLoginRequest request)
    {
        if (environment.IsDevelopment())
        {
            await HttpContext.SignInAsync(
                AdminAuthDefaults.CookieScheme,
                AdminCredentials.CreatePrincipal("development", AdminAuthDefaults.CookieScheme));
            return Ok(new { message = "Development 环境已写入会话 Cookie。" });
        }

        if (!AdminCredentials.VerifyLogin(request.Username, request.Password, options.Value))
        {
            return Unauthorized(new { message = "账号或密码无效。" });
        }

        await HttpContext.SignInAsync(
            AdminAuthDefaults.CookieScheme,
            AdminCredentials.CreatePrincipal(request.Username, AdminAuthDefaults.CookieScheme));
        return Ok(new { message = "已登录。可打开 Dashboard。" });
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("logout", Name = "AdminLogout")]
    [EndpointSummary("退出")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminAuthDefaults.CookieScheme);
        return Ok(new { message = "已退出。" });
    }
}
