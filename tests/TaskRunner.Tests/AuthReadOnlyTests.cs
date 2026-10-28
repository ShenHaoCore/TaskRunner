using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using TaskRunner.Api.Auth;

namespace TaskRunner.Tests;

/// <summary>
/// 票 03：鉴权三形态与只读拦截（开发规格主缝的安全面）。
/// 宿主 Production + AdminApiKey=demo-key——Development 直通逻辑不生效，认证/守卫/只读全真。
/// </summary>
public sealed class AuthReadOnlyTests
{
    private const string Key = "demo-key";
    private const string Username = "admin";
    private const string Password = "demo-pass";

    [Fact]
    public async Task Requests_WithoutCredentials_AreRejected()
    {
        using var app = NewApp();

        var response = await app.Client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_AcceptedViaHeaderCookieSessionAndBasic_Equivalently()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        // Header / 登录会话 Cookie / Basic 各自一个 client，同一端点必须等效放行
        var viaHeader = NewClient(app);
        viaHeader.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);

        var viaCookie = await LoginClientAsync(app);

        var viaBasic = NewClient(app);
        viaBasic.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Key}")));

        foreach (var client in new[] { viaHeader, viaCookie, viaBasic })
        {
            var response = await client.GetAsync("/api/tasks");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("demo-job", (await response.Content.ReadFromJsonAsync<JsonElement>())[0]
                .GetProperty("jobId").GetString());
        }
    }

    [Fact]
    public async Task LoginCookie_IsNotRawPassword()
    {
        using var app = NewApp();
        var login = await NewClient(app).PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var cookieHeader = string.Join("; ", setCookies);
        Assert.Contains(AdminAuthDefaults.CookieName, cookieHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, cookieHeader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPassword_Returns401_WithoutLeakingSecret()
    {
        using var app = NewApp();

        var login = await NewClient(app).PostAsJsonAsync("/api/auth/login", new { username = Username, password = "wrong-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var body = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
    }

    [Fact]
    public async Task ReadOnlyMode_BlocksNonGet_AndAllowsGet()
    {
        using var app = NewApp(readOnly: true);
        app.Seed(Seed.Demo());

        var authorized = Authorized(NewClient(app));

        var get = await authorized.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var pause = await authorized.PostAsync("/api/tasks/demo-job/pause", null);
        Assert.Equal(HttpStatusCode.Forbidden, pause.StatusCode);
        Assert.Contains("只读", await pause.Content.ReadAsStringAsync());

        var cron = await authorized.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = "0 * * * * *" });
        Assert.Equal(HttpStatusCode.Forbidden, cron.StatusCode);

        var trigger = await authorized.PostAsync("/api/tasks/demo-job/trigger", null);
        Assert.Equal(HttpStatusCode.Forbidden, trigger.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyModeOff_WritesReachable()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        var authorized = Authorized(NewClient(app));
        var response = await authorized.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = "0 30 9 * * *" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void MissingAdminKey_FailsFast_InProduction()
    {
        // 生产形态不配密钥：宿主启动即抛（AdminKeyGuardStartupFilter 的 fail-fast）
        var exception = Assert.Throws<InvalidOperationException>(() => new ContractApp(Environments.Production));
        Assert.Contains("Hangfire:AdminApiKey", exception.Message);
    }

    [Fact]
    public async Task Dashboard_RequiresCredentials_AndAcceptsApiKey()
    {
        using var app = NewApp();

        var anonymous = await app.Client.GetAsync("/taskrunner");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"TaskRunner\"",
            anonymous.Headers.WwwAuthenticate.ToString());

        var authorized = Authorized(NewClient(app));
        var response = await authorized.GetAsync("/taskrunner");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("任务调度中心", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_AnonymousBrowser_RedirectsToLogin_WithReturnUrl()
    {
        using var app = NewApp();

        var browserLike = NewClient(app);
        browserLike.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        var response = await browserLike.GetAsync("/taskrunner");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location!.ToString());
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location!.ToString());

        // 机器请求（无 text/html Accept）仍保持 401 + Basic 质询
        var machine = NewClient(app);
        var machineResponse = await machine.GetAsync("/taskrunner");
        Assert.Equal(HttpStatusCode.Unauthorized, machineResponse.StatusCode);
    }

    private static ContractApp NewApp(bool readOnly = false) => readOnly
        ? new ContractApp(Environments.Production,
            ("Hangfire:AdminApiKey", Key),
            ("Hangfire:AdminUsername", Username),
            ("Hangfire:AdminPassword", Password),
            ("Hangfire:ReadOnlyMode", "true"))
        : new ContractApp(Environments.Production,
            ("Hangfire:AdminApiKey", Key),
            ("Hangfire:AdminUsername", Username),
            ("Hangfire:AdminPassword", Password));

    private static HttpClient NewClient(ContractApp app) => app.CreateClient();

    private static HttpClient Authorized(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);
        return client;
    }

    private static async Task<HttpClient> LoginClientAsync(ContractApp app)
    {
        var client = NewClient(app);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var setCookies));

        // 测试宿主默认不自动存 Cookie；把登录签发的会话 Cookie 回写到后续请求。
        var parts = new List<string>();
        foreach (var header in setCookies)
        {
            var segment = header.Split(';', 2)[0].Trim();
            if (!string.IsNullOrEmpty(segment))
            {
                parts.Add(segment);
            }
        }

        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", parts));
        return client;
    }
}
