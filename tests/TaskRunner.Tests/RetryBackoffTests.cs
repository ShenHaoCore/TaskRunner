using TaskRunner.Hangfire;

namespace TaskRunner.Tests;

/// <summary>重试退避序列：2/4/8…指数翻倍，封顶 1 小时。</summary>
public sealed class RetryBackoffTests
{
    [Theory]
    [InlineData(0, new[] { 2 })] // attempts=0 不发生重试，序列形状仅为兼容既有装配
    [InlineData(1, new[] { 2 })]
    [InlineData(3, new[] { 2, 4, 8 })]
    [InlineData(6, new[] { 2, 4, 8, 16, 32, 64 })]
    public void CalculateDelays_DoublesExponentially(int attempts, int[] expected)
        => Assert.Equal(expected, RetryBackoff.CalculateDelays(attempts));

    [Fact]
    public void CalculateDelays_CapsAtOneHour()
    {
        var delays = RetryBackoff.CalculateDelays(20);

        Assert.Equal(20, delays.Length);
        Assert.All(delays, delay => Assert.True(delay <= RetryBackoff.MaxDelaySeconds));
        Assert.Equal(RetryBackoff.MaxDelaySeconds, delays[^1]);
    }
}
