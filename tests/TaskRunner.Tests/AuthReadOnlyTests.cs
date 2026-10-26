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

    [Fact]
    public async Task Requests_WithoutCredentials_AreRejected()
    {
        using var app = NewApp();

        var response = await app.Client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_AcceptedViaHeaderCookieAndBasic_Equivalently()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        // 三形态各自一个匿名 client，同一端点必须等效放行
        var viaHeader = NewClient(app);
        viaHeader.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);

        var viaCookie = NewClient(app);
        viaCookie.DefaultRequestHeaders.Add("Cookie", $"{AdminAuthDefaults.CookieName}={Key}");

        var viaBasic = NewClient(app);
        viaBasic.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{Key}")));

        foreach (var client in new[] { viaHeader, viaCookie, viaBasic })
        {
            var response = await client.GetAsync("/api/tasks");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("demo-job", (await response.Content.ReadFromJsonAsync<JsonElement>())[0]
                .GetProperty("jobId").GetString());
        }
    }

    [Fact]
    public async Task WrongKey_Returns401_WithoutLeakingSecret()
    {
        using var app = NewApp();

        var client = NewClient(app);
        client.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, "wrong-key");

        var response = await client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Key, body);
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

    private static ContractApp NewApp(bool readOnly = false) => readOnly
        ? new ContractApp(Environments.Production, ("Hangfire:AdminApiKey", Key), ("Hangfire:ReadOnlyMode", "true"))
        : new ContractApp(Environments.Production, ("Hangfire:AdminApiKey", Key));

    private static HttpClient NewClient(ContractApp app) => app.CreateClient();

    private static HttpClient Authorized(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);
        return client;
    }
}
