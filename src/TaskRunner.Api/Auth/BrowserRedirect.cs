namespace TaskRunner.Api.Auth;

/// <summary>
/// 「浏览器请求跳登录页」的统一判定与重定向：Accept 含 text/html 即视为浏览器。
/// Dashboard 中间件与 ApiKey 认证挑战共用，避免两处 returnUrl/判定规则漂移。
/// </summary>
public static class BrowserRedirect
{
    public static bool IsBrowserRequest(HttpRequest request) =>
        request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    /// <summary>302 到 /login 并携带 returnUrl（登录后回到原页面）。</summary>
    public static void RedirectToLogin(HttpResponse response, HttpRequest request)
    {
        var returnUrl = request.Path + request.QueryString;
        response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }
}
