using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Controllers;

/// <summary>交互式登录（账号密码表单）：签发会话 Cookie；机器调用仍走 AdminApiKey（Header / Basic）。</summary>
[AllowAnonymous]
public sealed class AccountController(
    IOptions<TaskRunnerOptions> options,
    IHostEnvironment environment) : Controller
{
    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl)
    {
        ViewData["Title"] = "登录 — TaskRunner";
        ViewData["NavActive"] = "login";

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        return View(new LoginViewModel(returnUrl));
    }

    [HttpPost("/login")]
    [EnableRateLimiting(AdminAuthDefaults.LoginRateLimitPolicy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? username, string? password, string? returnUrl)
    {
        ViewData["Title"] = "登录 — TaskRunner";
        ViewData["NavActive"] = "login";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "请输入账号与密码。");
        }
        else if (!environment.IsDevelopment()
                 && !AdminCredentials.VerifyLogin(username, password, options.Value))
        {
            ModelState.AddModelError(
                string.Empty,
                string.IsNullOrWhiteSpace(options.Value.AdminPassword)
                    ? "未配置登录密码：请在生产配置补充 Hangfire:AdminPassword 后重启。"
                    : "账号或密码无效。");
        }

        if (ModelState.IsValid)
        {
            await HttpContext.SignInAsync(
                AdminAuthDefaults.CookieScheme,
                AdminCredentials.CreatePrincipal(username!, AdminAuthDefaults.CookieScheme));
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        ViewBag.Username = username;
        return View(new LoginViewModel(returnUrl));
    }

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminAuthDefaults.CookieScheme);
        return Redirect("/");
    }

    /// <summary>3.5.0 及更早的静态页书签重定向。</summary>
    [HttpGet("/login.html")]
    public IActionResult LegacyLogin() => RedirectPermanent("/login");

    public sealed record LoginViewModel(string? ReturnUrl);
}
