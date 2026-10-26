using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskRunner.Api.Auth;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

/// <summary>
/// 3.2.x 加固面：/scalar 与 /openapi 的生产鉴权守卫、/info 的 environment 与 heartbeat 字段、
/// 登录接口限流（429）与心跳目录开关。
/// </summary>
public sealed class DocsGuardAndInfoTests
{
    private const string Key = "demo-key";

    // ── 文档守卫 ─────────────────────────────────────────────

    [Fact]
    public async Task Scalar_WithoutCredential_InProduction_Is401WithBasicChallenge()
    {
        using var app = NewProductionApp();

        var response = await app.Client.GetAsync("/scalar");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"TaskRunner\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task OpenApi_WithoutCredential_InProduction_Is401()
    {
        using var app = NewProductionApp();

        var response = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_WithAdminKey_InProduction_Returns200()
    {
        using var app = NewProductionApp();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Scalar_WithAdminKey_InProduction_IsAccessible()
    {
        using var app = NewProductionApp();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(AdminAuthDefaults.HeaderName, Key);

        var response = await client.GetAsync("/scalar");
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"实际状态码 {response.StatusCode}");
    }

    [Fact]
    public async Task Docs_InDevelopment_AreOpenWithoutCredential()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── /info：环境与心跳 ────────────────────────────────────

    [Fact]
    public async Task Info_InProduction_ShowsEnvironmentAndHeartbeat()
    {
        using var app = NewProductionApp();

        var json = await app.Client.GetStringAsync("/info");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Production", root.GetProperty("environment").GetString());
        Assert.Equal("/scalar", root.GetProperty("scalar").GetString());

        var heartbeat = root.GetProperty("heartbeat");
        Assert.True(heartbeat.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, heartbeat.GetProperty("lastExecution").ValueKind);
    }

    [Fact]
    public async Task Info_InDevelopment_ShowsDevelopmentEnvironment()
    {
        using var app = new ContractApp();

        var json = await app.Client.GetStringAsync("/info");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("Development", doc.RootElement.GetProperty("environment").GetString());
    }

    // ── 登录限流 ─────────────────────────────────────────────

    [Fact]
    public async Task Login_InProduction_IsRateLimited_AfterFiveAttemptsPerMinute()
    {
        using var app = NewProductionApp();

        HttpStatusCode? limited = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var response = await app.Client.PostAsJsonAsync("/api/auth/login", new { apiKey = "wrong-key" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response.StatusCode;
                break;
            }

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, limited);
    }

    [Fact]
    public async Task Login_InDevelopment_IsNotRateLimited()
    {
        using var app = new ContractApp();

        HttpStatusCode? limited = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var response = await app.Client.PostAsJsonAsync("/api/auth/login", new { apiKey = "whatever" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response.StatusCode;
                break;
            }
        }

        Assert.Null(limited);
    }

    // ── 心跳目录开关 ─────────────────────────────────────────

    [Fact]
    public void Heartbeat_EnabledByDefault_IsInCatalog()
    {
        using var provider = BuildCoreProvider(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=taskrunner;Username=u;Password=p"
        });

        var catalog = provider.GetRequiredService<IJobCatalog>();
        Assert.Contains(catalog.RecurringJobs, job => job.JobId == HeartbeatJob.JobIdValue);
    }

    [Fact]
    public void Heartbeat_Disabled_IsFilteredFromCatalog()
    {
        using var provider = BuildCoreProvider(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=taskrunner;Username=u;Password=p",
            ["Hangfire:HeartbeatEnabled"] = "false"
        });

        var catalog = provider.GetRequiredService<IJobCatalog>();
        Assert.DoesNotContain(catalog.RecurringJobs, job => job.JobId == HeartbeatJob.JobIdValue);
        Assert.Empty(catalog.RecurringJobs);
    }

    private static ServiceProvider BuildCoreProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddTaskRunnerCore(configuration);
        return services.BuildServiceProvider();
    }

    private static ContractApp NewProductionApp() =>
        new(Environments.Production, ("Hangfire:AdminApiKey", Key));
}
