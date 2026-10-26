using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Controllers;
using TaskRunner.Api.Filters;
using TaskRunner.Api.Hosting;
using TaskRunner.Api.Services;
using TaskRunner.Core;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;
using TaskRunner.Hangfire;

namespace TaskRunner.Tests;

/// <summary>
/// 票 02：REST /api/tasks 运维契约（开发规格主缝）。
/// 复刻 Program 的服务装配与请求管道（过滤器/认证/路由 100% 真实），
/// 存储换测试件：SQLite 文件库 + Hangfire MemoryStorage + 记录式调度器。
/// 鉴权与只读属票 03，故宿主跑在 Development（认证放行）。
/// </summary>
public sealed class RestContractTests
{
    [Fact]
    public async Task List_ReturnsSeededTasks_WithAllViewFields()
    {
        using var app = new ContractApp();
        app.Seed(Demo(), Demo("second-job", "0 0 12 * * *"));

        var body = await app.GetJsonAsync("/api/tasks");

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());
        var first = body[0];
        Assert.Equal("demo-job", first.GetProperty("jobId").GetString());
        Assert.Equal("DemoJob", first.GetProperty("jobName").GetString());
        Assert.Equal("*/5 * * * * *", first.GetProperty("cronExpr").GetString());
        Assert.True(first.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("演示任务", first.GetProperty("description").GetString());
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
        app.Seed(Demo());

        var response = await app.Client.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = "0 30 9 * * *" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 模拟重启：重新同步发现结果（特性默认值不变），运维覆盖必须保留
        app.Seed(Demo());
        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.Equal("0 30 9 * * *", body.GetProperty("cronExpr").GetString());
    }

    [Fact]
    public async Task UpdateCron_RejectsInvalid_WithoutChangingStore()
    {
        using var app = new ContractApp();
        app.Seed(Demo());

        foreach (var invalid in new[] { "* * * *", "* * * * * * *", "", "not a cron" })
        {
            var response = await app.Client.PutAsJsonAsync("/api/tasks/demo-job/cron", new { cron = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var message = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString();
            Assert.False(string.IsNullOrWhiteSpace(message));
        }

        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.Equal("*/5 * * * * *", body.GetProperty("cronExpr").GetString());
    }

    [Fact]
    public async Task Pause_PersistsDisabled_AndRemovesRecurring()
    {
        using var app = new ContractApp();
        app.Seed(Demo());

        var response = await app.Client.PostAsync("/api/tasks/demo-job/pause", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("demo-job", app.Scheduler.Removed);
        Assert.DoesNotContain(app.Scheduler.Added, call => call.StartsWith("demo-job|", StringComparison.Ordinal));

        // 重启后仍停用：重新同步不复活
        app.Seed(Demo());
        var body = await app.GetJsonAsync("/api/tasks/demo-job");
        Assert.False(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Resume_RestoresEnabled_AndRegistersRecurring()
    {
        using var app = new ContractApp();
        app.Seed(Demo());
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
        app.Seed(Demo());

        var response = await app.Client.PostAsync("/api/tasks/demo-job/trigger", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("demo-job", body.GetProperty("jobId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("queueJobId").GetString()));
        Assert.Contains("demo-job", app.Scheduler.Enqueued);
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
        app.Seed(Demo());

        var body = await app.GetJsonAsync("/api/tasks/demo-job/history?limit=5");
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
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

    private static RecurringJobDescriptor Demo(string jobId = "demo-job", string cron = "*/5 * * * * *", bool enabled = true) =>
        new(jobId, "DemoJob", cron, "Demo.Job, Demo", typeof(string), "演示任务", enabled);

    /// <summary>记录调度的 fake：断言「运维操作驱动 Hangfire」的可观察证据。</summary>
    private sealed class RecordingScheduler : ITaskScheduler
    {
        public List<string> Added { get; } = [];
        public List<string> Removed { get; } = [];
        public List<string> Enqueued { get; } = [];

        public void AddOrUpdateRecurring(string jobId, string jobType, string cron) => Added.Add($"{jobId}|{cron}");

        public void RemoveRecurring(string jobId) => Removed.Add(jobId);

        public IReadOnlyList<string> PruneRecurringExcept(IReadOnlyCollection<string> keepJobIds) => [];

        public string Enqueue(string jobId, string jobType)
        {
            Enqueued.Add(jobId);
            return $"queue-{Guid.NewGuid():N}";
        }

        public void Reset()
        {
            Added.Clear();
            Removed.Clear();
            Enqueued.Clear();
        }
    }

    /// <summary>
    /// 测试宿主：与 Program 相同的装配（角色 Combined、Development 认证放行），
    /// 管道经 UseTaskRunnerPipeline 100% 复用；存储与调度器换测试件，
    /// 不注册 RecurringJobBootstrapper（迁移与种子由测试自管）。
    /// </summary>
    private sealed class ContractApp : IDisposable
    {
        private readonly WebApplication _app;
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"taskrunner-rest-{Guid.NewGuid():N}.db");

        public ContractApp()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Hangfire:Storage"] = "PostgreSql"
            });

            builder.Services.AddTaskRunnerCore(builder.Configuration);
            builder.Services.AddTaskRunnerAdminAuth();
            builder.Services.AddSerilog(); // UseTaskRunnerPipeline 的请求日志中间件依赖 DiagnosticContext

            // 与生产同构的 Hangfire 配置，存储换 MemoryStorage（不调 AddTaskRunnerHangfire，
            // 其配置委托会立即解析 PostgreSql 连接串）；Dashboard 需要本调用注册的标记服务
            builder.Services.AddHangfire((_, config) => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMemoryStorage());

            // 测试件：SQLite 换掉 Npgsql、fake 调度器换掉 HangfireTaskScheduler
            builder.Services.RemoveAll(typeof(DbContextOptions<TaskRunnerDbContext>));
            builder.Services.RemoveAll<TaskRunnerDbContext>();
            builder.Services.RemoveAll(typeof(IDbContextOptionsConfiguration<TaskRunnerDbContext>));
            builder.Services.AddDbContext<TaskRunnerDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
            Scheduler = new RecordingScheduler();
            builder.Services.AddSingleton<ITaskScheduler>(Scheduler);

            // 与 Program 一致：管道、过滤器、监控读侧（不注册 RecurringJobBootstrapper）
            builder.Services.AddSingleton<HangfireMonitoringReader>();
            builder.Services.AddScoped<ReadOnlyGuardFilter>();
            builder.Services.AddProblemDetails();
            builder.Services.AddControllers(options => options.Filters.AddService<ReadOnlyGuardFilter>())
                .AddApplicationPart(typeof(TasksController).Assembly); // entry 是 testhost，需显式挂 Api 程序集
            builder.Services.AddOpenApi();

            _app = builder.Build();
            _app.UseTaskRunnerPipeline(HangfireHostRole.Combined);
            _app.Start();
            Client = _app.GetTestClient();

            // 与生产 bootstrapper 一致：启动即迁移业务表
            using var scope = _app.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>()
                .InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public RecordingScheduler Scheduler { get; }

        public HttpClient Client { get; }

        public void Seed(params RecurringJobDescriptor[] descriptors)
        {
            using var scope = _app.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
            repository.SyncDiscoveredAsync(descriptors, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<JsonElement> GetJsonAsync(string requestUri) =>
            await Client.GetFromJsonAsync<JsonElement>(requestUri);

        public void Dispose()
        {
            Client.Dispose();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }
}
