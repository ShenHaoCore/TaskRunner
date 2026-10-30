using System.Net;

namespace TaskRunner.Tests;

/// <summary>3.8.0 的 /health 真实健康检查：业务库连通 + Hangfire 存储可读 → Healthy。</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_ReturnsHealthy_WithBothChecksUp()
    {
        using var app = new ContractApp();

        var response = await app.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
