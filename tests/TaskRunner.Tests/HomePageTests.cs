using System.Net;
using System.Net.Http.Json;

namespace TaskRunner.Tests;

/// <summary>首页轻量化契约：健康信号 + 最近失败；统计图表让位 Dashboard，匿名不访问 Hangfire。</summary>
public sealed class HomePageTests
{
    [Fact]
    public async Task HomePage_Authenticated_ShowsHealthSignals_AndRecentFailures()
    {
        using var app = ContractApp.NewProductionApp();
        var client = await LoginAsync(app);
        app.Seed(Seed.Demo());

        var html = await client.GetStringAsync("/");
        // Razor 输出中文会 HTML 编码，先解码再断言
        html = WebUtility.HtmlDecode(html);

        Assert.Contains("系统状态", html, StringComparison.Ordinal);
        // 空存储无失败 → 失败列表整块不渲染，状态行显示「无失败记录」
        Assert.Contains("无失败记录", html, StringComparison.Ordinal);
        Assert.DoesNotContain("recent-block", html, StringComparison.Ordinal);
        // 已精简：不再渲染与 Dashboard 同源的指标卡与入口卡
        Assert.DoesNotContain("运行概览", html, StringComparison.Ordinal);
        Assert.DoesNotContain("metric-grid", html, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-grid", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_Anonymous_ShowsSkeletonWithLoginHint()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("系统状态", html, StringComparison.Ordinal);
        Assert.Contains("健康状态需要管理员鉴权", html, StringComparison.Ordinal);
        // 匿名不渲染失败列表（Hangfire 零访问）
        Assert.DoesNotContain("最近失败", html, StringComparison.Ordinal);
        Assert.DoesNotContain("recent-block", html, StringComparison.Ordinal);
    }

    private static async Task<HttpClient> LoginAsync(ContractApp app)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = TestCredentials.Username,
            password = TestCredentials.Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var cookie = string.Join("; ", login.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Select(v => v.Split(';', 2)[0])
            .Where(v => !string.IsNullOrEmpty(v)));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
