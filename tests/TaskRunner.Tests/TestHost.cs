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
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;
using TaskRunner.Hangfire;

namespace TaskRunner.Tests;

/// <summary>票 02/03 共享的发现结果种子。</summary>
internal static class Seed
{
    public static RecurringJobDescriptor Demo(
        string jobId = "demo-job",
        string cron = "*/5 * * * * *",
        bool enabled = true,
        string queue = "default") =>
        new(jobId, "DemoJob", cron, "Demo.Job, Demo", typeof(string), "演示任务", enabled, queue);
}

/// <summary>记录调度的 fake：断言「运维操作驱动 Hangfire」的可观察证据。</summary>
internal sealed class RecordingScheduler : ITaskScheduler
{
    public List<string> Added { get; } = [];
    public List<string> Removed { get; } = [];
    public List<string> Enqueued { get; } = [];
    public List<string> Scheduled { get; } = [];

    public void AddOrUpdateRecurring(string jobId, string jobType, string cron, string queue) =>
        Added.Add($"{jobId}|{cron}|{queue}");

    public void RemoveRecurring(string jobId) => Removed.Add(jobId);

    public IReadOnlyList<string> PruneRecurringExcept(IReadOnlyCollection<string> keepJobIds) => [];

    public string Enqueue(string jobId, string jobType, string queue)
    {
        Enqueued.Add($"{jobId}|{queue}");
        return $"queue-{Guid.NewGuid():N}";
    }

    public string Schedule(string jobId, string jobType, string queue, DateTimeOffset runAt)
    {
        Scheduled.Add($"{jobId}|{queue}|{runAt:O}");
        return $"sched-{Guid.NewGuid():N}";
    }

    public void Reset()
    {
        Added.Clear();
        Removed.Clear();
        Enqueued.Clear();
        Scheduled.Clear();
    }
}

/// <summary>
/// 票 02/03 共享测试宿主：复刻 Program 的装配（AddTaskRunnerCore + AddTaskRunnerAdminAuth +
/// UseTaskRunnerPipeline，过滤器/认证/路由 100% 真实），存储换测试件：SQLite 文件库 +
/// Hangfire MemoryStorage + 记录式调度器；不注册 RecurringJobBootstrapper（迁移由宿主自管、种子由测试自管）。
/// environment 缺省 Development（认证放行）；生产形态传 Production 并配 AdminApiKey（安全守卫同步生效）。
/// </summary>
internal sealed class ContractApp : IDisposable
{
    private readonly WebApplication _app;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"taskrunner-rest-{Guid.NewGuid():N}.db");

    public ContractApp(string? environment = null, params (string Key, string? Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment ?? Environments.Development
        });
        builder.WebHost.UseTestServer();

        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["Hangfire:Storage"] = "PostgreSql"
        };
        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(configuration);

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

    /// <summary>新建独立 HttpClient（供不同凭据形态各用一个 client）。</summary>
    public HttpClient CreateClient() => _app.GetTestClient();

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
