using System.Net;
using System.Text;
using TaskRunner.Bilibili;
using TaskRunner.Bilibili.Api;
using Xunit;

namespace TaskRunner.Tests;

public class BilibiliHttpClientRetryTests
{
    private const string SuccessJson = """{"code":0,"message":"0"}""";
    private const string RiskControlJson = """{"code":-412,"message":"请求被拦截"}""";

    private static BilibiliHttpClient CreateClient(StubHandler handler)
        => new(new HttpClient(handler), new StubCookieStore()) { RetryBaseDelay = TimeSpan.Zero };

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetAsync_网络异常后成功_自动重试()
    {
        var handler = new StubHandler(call => call < 3
            ? throw new HttpRequestException("连接重置")
            : Json(HttpStatusCode.OK, SuccessJson));

        var result = await CreateClient(handler).GetAsync("https://api.bilibili.com/x/test", CancellationToken.None);

        Assert.Equal(0, result.Code);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_服务端500_自动重试()
    {
        var handler = new StubHandler(call => call < 3
            ? Json(HttpStatusCode.InternalServerError, "{}")
            : Json(HttpStatusCode.OK, SuccessJson));

        var result = await CreateClient(handler).GetAsync("https://api.bilibili.com/x/test", CancellationToken.None);

        Assert.Equal(0, result.Code);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_风控412_不重试()
    {
        var handler = new StubHandler(_ => Json((HttpStatusCode)412, RiskControlJson));

        var result = await CreateClient(handler).GetAsync("https://api.bilibili.com/x/test", CancellationToken.None);

        Assert.Equal(-412, result.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_连续网络异常_重试耗尽后抛出()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("网络不可达"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateClient(handler).GetAsync("https://api.bilibili.com/x/test", CancellationToken.None));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetAsync_请求超时_自动重试()
    {
        var handler = new StubHandler(call => call < 2
            ? throw new TaskCanceledException("超时")
            : Json(HttpStatusCode.OK, SuccessJson));

        var result = await CreateClient(handler).GetAsync("https://api.bilibili.com/x/test", CancellationToken.None);

        Assert.Equal(0, result.Code);
        Assert.Equal(2, handler.CallCount);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;

        public StubHandler(Func<int, HttpResponseMessage> responder) => _responder = responder;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(++CallCount));
    }

    private sealed class StubCookieStore : IBilibiliCookieStore
    {
        public string FilePath => string.Empty;

        public string GetCookie() => "SESSDATA=test; bili_jct=test";

        public Task SaveAsync(string cookie, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
