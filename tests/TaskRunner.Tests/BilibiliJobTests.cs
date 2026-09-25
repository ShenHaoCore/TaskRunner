using TaskRunner.Bilibili;
using TaskRunner.Bilibili.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

public sealed class BilibiliJobTests
{
    [Fact]
    public void Scan_FindsDailyJob()
    {
        var jobs = RecurringJobScanner.Scan(typeof(BilibiliDailyJob).Assembly);
        var daily = Assert.Single(jobs, job => job.JobId == "bilibili-daily");
        Assert.True(daily.EnabledByDefault);
        Assert.Equal("0 0 9 * * *", daily.Cron);
        Assert.True(typeof(BilibiliDailyJob).IsDefined(typeof(ExclusiveExecutionAttribute), inherit: false));
    }

    [Fact]
    public void Cookie_ParsesLoginTokens()
    {
        var cookie = new BiliCookie("SESSDATA=abc; bili_jct=token123; other=1");
        Assert.True(cookie.HasLoginTokens);
        Assert.Equal("abc", cookie.SessData);
        Assert.Equal("token123", cookie.BiliJct);
    }

    [Fact]
    public void Cookie_FromSetCookieHeaders_KeepsNameValuePairs()
    {
        var cookie = BiliCookie.FromSetCookieHeaders(
        [
            "SESSDATA=sess; Path=/; Domain=.bilibili.com; HttpOnly",
            "bili_jct=jct; Path=/",
            "DedeUserID=123; Path=/"
        ]);
        var parsed = new BiliCookie(cookie);
        Assert.True(parsed.HasLoginTokens);
        Assert.Equal("sess", parsed.SessData);
        Assert.Equal("jct", parsed.BiliJct);
        Assert.Equal("123", parsed.DedeUserId);
    }

    [Fact]
    public void Cookie_MissingTokens_IsInvalid()
    {
        Assert.False(new BiliCookie("SESSDATA=only").HasLoginTokens);
        Assert.False(new BiliCookie("").HasLoginTokens);
    }

    [Fact]
    public void Cookie_MergeSetCookies_AddsBuvid()
    {
        var cookie = new BiliCookie("SESSDATA=abc; bili_jct=token123");
        var merged = cookie.MergeSetCookies(["buvid3=xyz; Path=/", "b_nut=1; Path=/"]);
        Assert.True(merged.HasLoginTokens);
        Assert.True(merged.HasBuvid);
        Assert.Equal("xyz", merged.Buvid3);
    }
}
