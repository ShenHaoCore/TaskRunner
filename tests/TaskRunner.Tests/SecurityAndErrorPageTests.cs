using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskRunner.Tests;

/// <summary>安全响应头兜底与统一错误页（页面渲染 / /api ProblemDetails）。</summary>
public sealed class SecurityAndErrorPageTests
{
    [Fact]
    public async Task Responses_CarrySecurityHeaderBaseline()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DENY", response.Headers.NonValidated["X-Frame-Options"].ToString());
        Assert.Equal("frame-ancestors 'none'", response.Headers.NonValidated["Content-Security-Policy"].ToString());
        Assert.Equal("nosniff", response.Headers.NonValidated["X-Content-Type-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.NonValidated["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task PageRoute_UnknownPath_RendersBranded404()
    {
        using var app = new ContractApp();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        var response = await client.GetAsync("/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Razor 会转义中文标题，先解码再断言
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("页面不存在", html, StringComparison.Ordinal);
        Assert.Contains("返回首页", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRoute_UnknownPath_ReturnsProblemDetails()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Contains("/api/no-such-endpoint", json.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRoute_401_TitleMatchesStatusSemantics()
    {
        using var app = ContractApp.NewProductionApp();

        // 401 经 StatusCodePages 重执行 /error/401：标题必须是「需要认证」而非「服务器内部错误」
        var response = await app.Client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, json.GetProperty("status").GetInt32());
        Assert.Equal("需要认证", json.GetProperty("title").GetString());
    }
}
