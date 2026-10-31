using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Api.Auth;

namespace TaskRunner.Api.Controllers;

/// <summary>
/// 错误页端点：UseExceptionHandler("/error") 与 UseStatusCodePagesWithReExecute("/error/{0}") 的落点。
/// /api 前缀始终返回 ProblemDetails（机器客户端）；页面请求渲染品牌化错误页。
/// </summary>
[AllowAnonymous]
public sealed class ErrorController : Controller
{
    // 不限 HTTP 方法：StatusCodePages 重执行保留原始动词（如登录 POST 的 401/429），限 GET 会变 405。
    [Route("/error")]
    [Route("/error/{code:int}")]
    public IActionResult Index(int? code)
    {
        var statusCode = code ?? StatusCodes.Status500InternalServerError;
        if (statusCode is < 400 or > 599)
        {
            statusCode = StatusCodes.Status500InternalServerError;
        }

        // 重执行后的原始请求路径：状态码页来自 IStatusCodeReExecuteFeature，异常页来自 IExceptionHandlerPathFeature。
        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
            ?? HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path
            ?? string.Empty;

        if (originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: statusCode,
                title: Describe(statusCode),
                detail: $"请求路径 {originalPath} 返回 {statusCode}。");
        }

        if (!BrowserRedirect.IsBrowserRequest(Request))
        {
            // 机器客户端打到页面路由（如 curl 首页 404）：同样回 JSON，避免解析 HTML。
            return Problem(statusCode: statusCode, title: "请求失败", detail: $"请求返回 {statusCode}。");
        }

        Response.StatusCode = statusCode;
        return View("Error", new ErrorViewModel(
            statusCode,
            statusCode switch
            {
                StatusCodes.Status404NotFound => "页面不存在",
                StatusCodes.Status401Unauthorized => "需要登录",
                StatusCodes.Status403Forbidden => "没有权限",
                StatusCodes.Status500InternalServerError => "服务器内部错误",
                _ => "请求失败"
            }));
    }

    /// <summary>按状态段给语义标题：401/403/429 经 StatusCodePages 重执行到这里时不能被标成 5xx 语义。</summary>
    private static string Describe(int statusCode) => statusCode switch
    {
        StatusCodes.Status404NotFound => "资源不存在",
        StatusCodes.Status401Unauthorized => "需要认证",
        StatusCodes.Status403Forbidden => "没有权限",
        StatusCodes.Status429TooManyRequests => "请求过于频繁",
        < 500 => "请求被拒绝",
        _ => "服务器内部错误"
    };

    public sealed record ErrorViewModel(int StatusCode, string Title);
}
