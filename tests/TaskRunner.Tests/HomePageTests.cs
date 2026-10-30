using System.Net;
using System.Net.Http.Json;

namespace TaskRunner.Tests;

/// <summary>首页改版契约：hero/活数据模块（趋势 + 最近执行）随登录态正确渲染。</summary>
public sealed class HomePageTests
{
    [Fact]
    public async Task HomePage_Authenticated_RendersLiveModules()
    {
        using var app = ContractApp.NewProductionApp();
        var client = await LoginAsync(app);
        app.Seed(Seed.Demo());

        var html = await client.GetStringAsync("/");

        Assert.Contains("text-gradient", html, StringComparison.Ordinal);
        Assert.Contains("运行概览", html, StringComparison.Ordinal);
        Assert.Contains("已预约", html, StringComparison.Ordinal);
        // 空存储 → 趋势与最近执行显示空态而非缺模块
        Assert.Contains("暂无执行数据", html, StringComparison.Ordinal);
        Assert.Contains("暂无执行记录", html, StringComparison.Ordinal);
        Assert.Contains("最近执行", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_Anonymous_ShowsSkeletonWithLoginHint()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("运行概览", html, StringComparison.Ordinal);
        Assert.Contains("统计需要管理员鉴权", html, StringComparison.Ordinal);
        // 匿名不渲染活数据模块（Hangfire 零访问）
        Assert.DoesNotContain("最近执行", html, StringComparison.Ordinal);
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
