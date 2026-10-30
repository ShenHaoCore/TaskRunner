using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Api.Services;

namespace TaskRunner.Tests;

/// <summary>
/// 监控读侧薄封装的优雅降级：空 MemoryStorage 上最近执行返回空列表且不抛
/// （首页/运行台依赖这些方法，存储抖动不能拖垮页面渲染）。
/// </summary>
public sealed class MonitoringReaderTests
{
    [Fact]
    public void RecentExecutions_OnEmptyStorage_ReturnsEmptyWithoutThrow()
    {
        using var app = new ContractApp();
        var reader = app.Services.CreateScope().ServiceProvider.GetRequiredService<HangfireMonitoringReader>();

        Assert.Empty(reader.ReadRecentExecutions());
        Assert.Empty(reader.ReadRecentExecutions(3));
        Assert.Empty(reader.ReadRecentExecutions(5, failuresOnly: true));
    }
}
