using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskRunner.Tests;

/// <summary>
/// 票 02：REST /api/tasks 运维契约（开发规格主缝）。
/// 真实 HTTP 管道 + SQLite 文件库 + Hangfire MemoryStorage（宿主见 TestHost.cs）；
/// 鉴权与只读属票 03，故宿主跑在 Development（认证放行）。
/// </summary>
public sealed class RestContractTests
{
    [Fact]
    public async Task List_ReturnsSeededTasks_WithAllViewFields()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo(), Seed.Demo("second-job", "0 0 12 * * *"));

        var body = await app.GetJsonAsync("/api/tasks");

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());
        var first = body[0];
        Assert.Equal("demo-job", first.GetProperty("jobId").GetString());
        Assert.Equal("DemoJob", first.GetProperty("jobName").GetString());
        Assert.Equal("*/5 * * * * *", first.GetProperty("cronExpr").GetString());
        Assert.True(first.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("演示任务", first.GetProperty("description").GetString());
        Assert.Equal("default", first.GetProperty("queue").GetString());
        // MonitoringApi 侧字段（MemoryStorage 无执行记录，应为 null 而非缺失）
        Assert.Equal(JsonValueKind.Null, first.GetProperty("lastExecution").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("nextExecution").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("lastJobState").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task CronOverride_WinsOverDefault_AndPersistsAcrossResync()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        var response = await app.Client.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = "0 30 9 * * *" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 模拟重启：重新同步发现结果（特性默认值不变），运维覆盖必须保留
        app.Seed(Seed.Demo());
        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.Equal("0 30 9 * * *", body.GetProperty("cronExpr").GetString());
    }

    [Fact]
    public async Task UpdateCron_RejectsInvalid_WithoutChangingStore()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        foreach (var invalid in new[] { "* * * *", "* * * * * * *", "", "not a cron" })
        {
            var response = await app.Client.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
            Assert.False(string.IsNullOrWhiteSpace(detail));
        }

        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.Equal("*/5 * * * * *", body.GetProperty("cronExpr").GetString());
    }

    [Fact]
    public async Task Pause_PersistsDisabled_AndRemovesRecurring()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        var response = await app.Client.PostAsync("/api/tasks/demo-job/pause", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("demo-job", app.Scheduler.Removed);
        Assert.DoesNotContain(app.Scheduler.Added, call => call.StartsWith("demo-job|", StringComparison.Ordinal));

        // 重启后仍停用：重新同步不复活
        app.Seed(Seed.Demo());
        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.False(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Resume_RestoresEnabled_AndRegistersRecurring()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());
        await app.Client.PostAsync("/api/tasks/demo-job/pause", null);
        app.Scheduler.Reset();

        var response = await app.Client.PostAsync("/api/tasks/demo-job/resume", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isEnabled").GetBoolean());
        Assert.Contains(app.Scheduler.Added, call => call.StartsWith("demo-job|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trigger_EnqueuesJob_AndReturnsQueueJobId()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        var response = await app.Client.PostAsync("/api/tasks/demo-job/trigger", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("demo-job", body.GetProperty("jobId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("queueJobId").GetString()));
        Assert.Contains("demo-job|default", app.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Trigger_WithQueue_EnqueuesToDeclaredQueue()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo(queue: "critical"));

        var response = await app.Client.PostAsync("/api/tasks/demo-job/trigger", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("demo-job|critical", app.Scheduler.Enqueued);

        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.Equal("critical", body.GetProperty("queue").GetString());
    }

    [Fact]
    public async Task Trigger_WithRunAt_SchedulesOnce()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());
        var runAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var response = await app.Client.PostAsJsonAsync("/api/tasks/demo-job/trigger", new { runAt });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("scheduled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("queueJobId").GetString()));
        Assert.Contains(app.Scheduler.Scheduled, item => item.StartsWith("demo-job|default|", StringComparison.Ordinal));
        Assert.Empty(app.Scheduler.Enqueued);
    }

    [Fact]
    public async Task Trigger_RejectsPastRunAt_AndBothRunAtAndDelay()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        var past = await app.Client.PostAsJsonAsync(
            "/api/tasks/demo-job/trigger",
            new { runAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var both = await app.Client.PostAsJsonAsync(
            "/api/tasks/demo-job/trigger",
            new { runAt = DateTimeOffset.UtcNow.AddMinutes(5), delaySeconds = 60 });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Empty(app.Scheduler.Scheduled);
    }

    [Fact]
    public async Task Trigger_PausedTask_Returns409Conflict()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        await app.Client.PostAsync("/api/tasks/demo-job/pause", null);
        var response = await app.Client.PostAsync("/api/tasks/demo-job/trigger", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("已暂停", await response.Content.ReadAsStringAsync());
        Assert.Empty(app.Scheduler.Enqueued);
    }

    [Fact]
    public async Task List_SupportsPagination_AndETagNegotiation()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo(), Seed.Demo("second-job", "0 0 12 * * *"));

        var full = await app.Client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var etag = full.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(etag));
        Assert.Equal("2", full.Headers.GetValues("X-Total-Count").Single());

        // 命中 If-None-Match → 304
        var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        conditional.Headers.IfNoneMatch.ParseAdd(etag);
        var notModified = await app.Client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        // 分页：pageSize=1 取第一页
        var paged = await app.Client.GetAsync("/api/tasks?page=1&pageSize=1");
        Assert.Equal("2", paged.Headers.GetValues("X-Total-Count").Single());
        var body = await paged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetArrayLength());
    }

    [Fact]
    public async Task Statistics_ReturnsAllCounters()
    {
        using var app = new ContractApp();

        var body = await app.GetJsonAsync("/api/tasks/statistics");
        foreach (var counter in new[] { "succeeded", "failed", "enqueued", "processing", "scheduled", "recurring", "servers" })
        {
            Assert.True(body.TryGetProperty(counter, out var value), $"缺少统计字段 {counter}");
            Assert.Equal(JsonValueKind.Number, value.ValueKind);
        }
    }

    [Fact]
    public async Task History_ReturnsEmptyArray_WhenNoExecutions()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo());

        var body = await app.GetJsonAsync("/api/tasks/demo-job/history?limit=5");
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task CronPreview_ReturnsFiveFutureOccurrences_ForSupportedFormats()
    {
        using var app = new ContractApp();

        // 6 位（含秒）/ 5 位（分钟级）/ @宏 三种受支持形态
        foreach (var expr in new[] { "*/10 * * * * *", "*/30 * * * *", "@daily" })
        {
            var body = await app.GetJsonAsync($"/api/tasks/cron-preview?expr={Uri.EscapeDataString(expr)}");
            Assert.Equal(JsonValueKind.Array, body.GetProperty("occurrences").ValueKind);
            Assert.Equal(5, body.GetProperty("occurrences").GetArrayLength());
        }

        // 逐次递增：下一次总在上一次之后
        var occurrences = (await app.GetJsonAsync("/api/tasks/cron-preview?expr=%2A%2F10%20%2A%20%2A%20%2A%20%2A%20%2A"))
            .GetProperty("occurrences").EnumerateArray()
            .Select(item => item.GetDateTime())
            .ToList();
        Assert.Equal(5, occurrences.Count);
        Assert.Equal(occurrences.OrderBy(t => t), occurrences);
    }

    [Fact]
    public async Task CronPreview_RejectsInvalidExpression_With400Problem()
    {
        using var app = new ContractApp();

        foreach (var expr in new[] { "* * * *", "not a cron", "" })
        {
            var response = await app.Client.GetAsync($"/api/tasks/cron-preview?expr={Uri.EscapeDataString(expr)}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Cron", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task List_ETag_ChangesOnSameSecondWrites_AndPerPage()
    {
        using var app = new ContractApp();
        app.Seed(Seed.Demo(), Seed.Demo("second-job", "0 0 12 * * *"));

        var first = await app.Client.GetAsync("/api/tasks");
        var etag = first.Headers.ETag!.Tag;

        // 同一秒内连续两次写：旧的「数量+秒级截断」指纹会误命中 304，客户端拿到过期缓存
        await app.Client.PostAsync("/api/tasks/demo-job/pause", null);
        await app.Client.PostAsync("/api/tasks/second-job/pause", null);

        var revalidate = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        revalidate.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag));
        var second = await app.Client.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // 不同分页是不同表示，各自持有 ETag
        var paged = await app.Client.GetAsync("/api/tasks?page=1&pageSize=1");
        Assert.NotEqual(etag, paged.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task UnknownJobId_MapsTo404_WithMessage()
    {
        using var app = new ContractApp();

        var get = await app.Client.GetAsync("/api/tasks/missing-job");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Contains("missing-job", await get.Content.ReadAsStringAsync());

        var history = await app.Client.GetAsync("/api/tasks/missing-job/history");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);

        var trigger = await app.Client.PostAsync("/api/tasks/missing-job/trigger", null);
        Assert.Equal(HttpStatusCode.NotFound, trigger.StatusCode);

        var pause = await app.Client.PostAsync("/api/tasks/missing-job/pause", null);
        Assert.Equal(HttpStatusCode.NotFound, pause.StatusCode);

        var cron = await app.Client.PutAsJsonAsync("/api/tasks/missing-job/cron", new { cron = "0 * * * * *" });
        Assert.Equal(HttpStatusCode.NotFound, cron.StatusCode);
    }
}
