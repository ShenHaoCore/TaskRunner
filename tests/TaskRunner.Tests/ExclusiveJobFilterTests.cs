using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Hangfire;

namespace TaskRunner.Tests;

/// <summary>互斥过滤器锁键解析分支：缺参 / 参数形态不符 / 未知类型（告警）/ 非互斥 / 互斥。</summary>
public sealed class ExclusiveJobFilterTests
{
    [Fact]
    public void Args_TooShort_ReturnsFalse()
    {
        var filter = new ExclusiveJobFilter();
        Assert.False(filter.TryResolveLockKey(["only-one"], out _, out _));
    }

    [Fact]
    public void Args_NotString_ReturnsFalse()
    {
        var filter = new ExclusiveJobFilter();
        Assert.False(filter.TryResolveLockKey([42, "T, A"], out _, out _));
    }

    [Fact]
    public void UnknownType_ReturnsFalse_AndLogsWarning()
    {
        var logger = new RecordingLogger<ExclusiveJobFilter>();
        var filter = new ExclusiveJobFilter(logger);

        // 类型解析失败时互斥静默失效——必须有 Warning 可观测
        Assert.False(filter.TryResolveLockKey(["demo", "Missing.Type, Nowhere"], out _, out _));
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Missing.Type", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainJob_ReturnsFalse()
    {
        var filter = new ExclusiveJobFilter();
        Assert.False(filter.TryResolveLockKey(["demo", JobTypeName.For(typeof(PlainJob))], out _, out _));
    }

    [Fact]
    public void ExclusiveJob_ReturnsLockKeyAndTimeout()
    {
        var filter = new ExclusiveJobFilter();
        var ok = filter.TryResolveLockKey(["demo-job", JobTypeName.For(typeof(GuardedJob))], out var lockKey, out var timeout);

        Assert.True(ok);
        Assert.Equal("exclusive-job:demo-job", lockKey);
        Assert.Equal(TimeSpan.FromSeconds(20), timeout);
    }

    private sealed class PlainJob;

    [ExclusiveExecution(20)]
    private sealed class GuardedJob;
}
