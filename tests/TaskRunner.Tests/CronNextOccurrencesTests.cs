using TaskRunner.Core.Common;
using Xunit;

namespace TaskRunner.Tests;

/// <summary>Cron 预览（NextOccurrences）：秒级解析、@宏、闰日、count 钳制与非法输入。</summary>
public sealed class CronNextOccurrencesTests
{
    [Fact]
    public void SixFieldExpression_TicksAtSecondResolution()
    {
        var next = CronExpressionGuard.NextOccurrences("*/5 * * * * *", 3);

        Assert.Equal(3, next.Count);
        for (var i = 1; i < next.Count; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), next[i] - next[i - 1]);
        }
    }

    [Fact]
    public void MacroDaily_ReturnsMidnightUtc()
    {
        var next = CronExpressionGuard.NextOccurrences("@daily", 2);

        Assert.Equal(2, next.Count);
        Assert.All(next, item => Assert.Equal(TimeSpan.Zero, item.TimeOfDay));
        Assert.Equal(TimeSpan.FromDays(1), next[1] - next[0]);
    }

    [Fact]
    public void LeapDayExpression_LandsOnFebruary29()
    {
        var next = CronExpressionGuard.NextOccurrences("0 0 29 2 *", 1);

        var occurrence = Assert.Single(next);
        Assert.Equal(2, occurrence.Month);
        Assert.Equal(29, occurrence.Day);
    }

    [Theory]
    [InlineData(0, 1)]   // 下限：0 被钳制为 1
    [InlineData(50, 10)] // 上限：超过 10 被钳制为 10
    public void CountIsClamped(int requested, int expected)
    {
        var next = CronExpressionGuard.NextOccurrences("* * * * *", requested);

        Assert.Equal(expected, next.Count);
    }

    [Fact]
    public void InvalidExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CronExpressionGuard.NextOccurrences("not-a-cron", 3));
    }
}
