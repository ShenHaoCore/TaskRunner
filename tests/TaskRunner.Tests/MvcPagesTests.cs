using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

/// <summary>
/// MVC 页面缝：账号密码登录（表单 + 防伪令牌 + 限流）→ 会话 Cookie → 运行台/触发。
/// 宿主 Production + 账号密码齐备，与生产装配同构。
/// </summary>
public sealed class MvcPagesTests
{
    private static string Username => TestCredentials.Username;
    private static string Password => TestCredentials.Password;
    private const string TokenField = "__RequestVerificationToken";

    [Fact]
    public async Task Run_Anonymous_IsChallenged()
    {
        using var app = NewApp();
        var response = await app.Client.GetAsync("/run");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Run_AnonymousBrowser_RedirectsToLogin_WithReturnUrl()
    {
        using var app = NewApp();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        var response = await client.GetAsync("/run");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_Get_ReturnsAccountPasswordForm()
    {
        using var app = NewApp();

        var response = await app.Client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("管理员登录", html, StringComparison.Ordinal);
        Assert.Contains("name=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_Post_WrongPassword_RediscoversWithError()
    {
        using var app = NewApp();

        var response = await PostFormAsync(app, "/login", new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = "wrong-pass"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(body.Contains("账号或密码无效"), body);
    }

    [Fact]
    public async Task Login_Post_WithoutConfiguredPassword_ShowsGuidance()
    {
        // 故意不配 AdminPassword：生产未配置密码时登录页给出引导文案
        using var app = new ContractApp(
            Environments.Production,
            ("Hangfire:AdminApiKey", TestCredentials.Key),
            ("Hangfire:AdminUsername", Username));

        var response = await PostFormAsync(app, "/login", new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = "whatever"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(body.Contains("未配置登录密码"), body);
    }

    [Fact]
    public async Task Login_Post_Success_IssuesSession_AndRunPage_ShowsTasks()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        var client = await LoginAsync(app);

        var run = await client.GetAsync("/run?q=demo");
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        // Razor 输出中文会 HTML 编码，先解码再断言
        var html = WebUtility.HtmlDecode(await run.Content.ReadAsStringAsync());
        Assert.Contains("任务运行台", html, StringComparison.Ordinal);
        Assert.Contains("demo-job", html, StringComparison.Ordinal);
        Assert.Contains("立即运行", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trigger_Post_Enqueues_AndFlashesResult()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        var client = await LoginAsync(app);
        var runPage = await client.GetStringAsync("/run");
        var token = ExtractAntiForgeryToken(runPage);

        var response = await PostFormAsync(
            app, "/run/demo-job/trigger",
            new Dictionary<string, string> { [TokenField] = token },
            client, formPageUri: "/run");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        CopyCookies(response, client); // TempData 随 Cookie 传递，跟随重定向前必须回写

        // 触发确实驱动了调度器（可观察证据），并随重定向展示一次性提示
        var enqueued = Assert.Single(app.Scheduler.Enqueued);
        Assert.StartsWith("demo-job|", enqueued);

        var followed = await client.GetAsync(response.Headers.Location!);
        Assert.Contains("已入队 demo-job", WebUtility.HtmlDecode(await followed.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Logout_Post_ClearsSession()
    {
        using var app = NewApp();
        var client = await LoginAsync(app);

        var runPage = await client.GetStringAsync("/run");
        var token = ExtractAntiForgeryToken(runPage);
        var logout = await PostFormAsync(
            app, "/logout",
            new Dictionary<string, string> { [TokenField] = token },
            client, formPageUri: "/run");
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        // 无状态 Cookie 的注销=下发删除头（expires 过去时）；真实浏览器据此丢弃会话
        var setCookie = string.Join("; ", logout.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value));
        Assert.Contains(AdminCookieName(), setCookie, StringComparison.Ordinal);
        Assert.Contains("1970", setCookie, StringComparison.Ordinal);
    }

    private static ContractApp NewApp() => ContractApp.NewProductionApp();

    [Fact]
    public async Task UpdateCron_Post_Persists_AndFlashes()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        var client = await LoginAsync(app);
        var response = await PostFormAsync(
            app, "/run/demo-job/cron",
            new Dictionary<string, string> { ["cron"] = "0 45 10 * * *" },
            client, formPageUri: "/run");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        CopyCookies(response, client);

        // PRG 回来展示一次性提示
        var followed = await client.GetAsync(response.Headers.Location!);
        var body = WebUtility.HtmlDecode(await followed.Content.ReadAsStringAsync());
        Assert.Contains("已更新 demo-job 的 Cron", body);

        // 库中持久化（xmin 通道）
        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        var stored = await repository.FindAsync("demo-job", CancellationToken.None);
        Assert.Equal("0 45 10 * * *", stored!.CronExpr);
    }

    [Fact]
    public async Task UpdateCron_Post_InvalidCron_FlashesError_WithoutChange()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo());

        var client = await LoginAsync(app);
        var response = await PostFormAsync(
            app, "/run/demo-job/cron",
            new Dictionary<string, string> { ["cron"] = "not a cron" },
            client, formPageUri: "/run");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        CopyCookies(response, client);

        var followed = await client.GetAsync(response.Headers.Location!);
        var body = WebUtility.HtmlDecode(await followed.Content.ReadAsStringAsync());
        Assert.Contains("Cron 无效", body);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        var stored = await repository.FindAsync("demo-job", CancellationToken.None);
        Assert.Equal("*/5 * * * * *", stored!.CronExpr);
    }

    [Fact]
    public async Task Bulk_Pause_DisablesSelectedTasks_AndFlashesSummary()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo(), Seed.Demo("second-job", "0 0 12 * * *"));

        var client = await LoginAsync(app);
        // 勾选项在表格外由 JS 写入隐藏域；测试直接以两个 jobIds 键值对模拟提交
        var fields = new List<KeyValuePair<string, string>>
        {
            new("action", "pause"),
            new("jobIds", "demo-job"),
            new("jobIds", "second-job")
        };
        var response = await PostFormAsync(app, "/run/bulk", fields, client, formPageUri: "/run");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        CopyCookies(response, client);

        var followed = await client.GetAsync(response.Headers.Location!);
        var body = WebUtility.HtmlDecode(await followed.Content.ReadAsStringAsync());
        Assert.Contains("暂停完成：成功 2。", body);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        Assert.False((await repository.FindAsync("demo-job", CancellationToken.None))!.IsEnabled);
        Assert.False((await repository.FindAsync("second-job", CancellationToken.None))!.IsEnabled);
    }

    [Fact]
    public async Task Run_QueueFilter_ShowsOnlyMatchingTasks()
    {
        using var app = NewApp();
        app.Seed(Seed.Demo(), Seed.Demo("q-job", "0 0 13 * * *", queue: "critical"));

        var client = await LoginAsync(app);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/run?queue=critical"));

        Assert.Contains("q-job", html, StringComparison.Ordinal);
        Assert.DoesNotContain("demo-job", html, StringComparison.Ordinal);
    }

    /// <summary>表单登录：取防伪令牌 → POST → 回写会话 Cookie。</summary>
    private static async Task<HttpClient> LoginAsync(ContractApp app)
    {
        var client = app.CreateClient();
        var response = await PostFormAsync(app, "/login", new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = Password
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        CopyCookies(response, client);
        return client;
    }

    /// <summary>表单 POST 需要防伪令牌：先 GET 表单页，抓隐藏域并回写防伪 Cookie，再 POST。</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        ContractApp app, string postUri, IEnumerable<KeyValuePair<string, string>> fields,
        HttpClient? client = null, string? formPageUri = null)
    {
        client ??= app.CreateClient();
        formPageUri ??= postUri;

        var page = await client.GetAsync(formPageUri);
        var html = await page.Content.ReadAsStringAsync();
        // List 保留重复键（如 bulk 的多个 jobIds）；防伪令牌以本次 GET 的为准（去旧防重）
        var payload = new List<KeyValuePair<string, string>>(
            fields.Where(field => field.Key != TokenField))
        {
            new(TokenField, ExtractAntiForgeryToken(html))
        };
        CopyCookies(page, client);

        using var form = new FormUrlEncodedContent(payload);
        return await client.PostAsync(postUri, form);
    }

    private static void CopyCookies(HttpResponseMessage response, HttpClient client)
    {
        foreach (var cookie in response.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Select(v => v.Split(';', 2)[0])
            .Where(v => !string.IsNullOrEmpty(v)))
        {
            try
            {
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }
            catch (InvalidOperationException)
            {
                // 已存在同名头则跳过（Cookie 头由测试宿主自行累积的场景）
            }
        }
    }

    private static string AdminCookieName() =>
        TaskRunner.Api.Auth.AdminAuthDefaults.CookieName;

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "页面缺少防伪令牌。");
        return match.Groups[1].Value;
    }
}
