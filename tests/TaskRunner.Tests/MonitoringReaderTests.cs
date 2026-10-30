using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Api.Services;

namespace TaskRunner.Tests;

/// <summary>
/// 监控读侧新增薄封装的优雅降级：空 MemoryStorage 上趋势/最近执行返回空列表且不抛
/// （首页依赖这两个方法，存储抖动不能拖垮页面渲染）。
/// </summary>
public sealed class MonitoringReaderTests
{
    [Fact]
    public void HourlyTrend_OnEmptyStorage_ReturnsEmptyWithoutThrow()
    {
        using var app = new ContractApp();

        var reader = app.Services.CreateScope().ServiceProvider.GetRequiredService<HangfireMonitoringReader>();

        var trend = reader.ReadHourlyTrend();
        Assert.NotNull(trend);

        var limited = reader.ReadHourlyTrend(6);
        Assert.NotNull(limited);
        Assert.True(limited.Count <= 6);
    }

    [Fact]
    public void RecentExecutions_OnEmptyStorage_ReturnsEmptyWithoutThrow()
    {
        using var app = new ContractApp();

        var reader = app.Services.CreateScope().ServiceProvider.GetRequiredService<HangfireMonitoringReader>();

        var recent = reader.ReadRecentExecutions();
        Assert.Empty(recent);

        Assert.Empty(reader.ReadRecentExecutions(3));
    }
}
