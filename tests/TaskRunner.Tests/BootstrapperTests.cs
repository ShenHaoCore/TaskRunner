using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

/// <summary>
/// 启动编排（RecurringJobBootstrapper）：迁移 → 同步 → 清理 → 注册的各分支与隔离性。
/// SQLite 直建宿主 + RecordingScheduler + 捕获日志；RunUnderBootstrapLockAsync 的锁内执行
/// 由每个用例隐式覆盖（SyncAsync 整体在锁内跑，SQLite 直接执行 body）。
/// </summary>
public sealed class BootstrapperTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"taskrunner-boot-{Guid.NewGuid():N}.db");
    private ServiceProvider? _provider;

    private (RecurringJobBootstrapper Bootstrapper, RecordingLogger<RecurringJobBootstrapper> Logger) Build(
        IReadOnlyList<RecurringJobDescriptor> jobs,
        ITaskScheduler scheduler,
        Action<TaskRunnerOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TaskRunnerDbContext>(o => o.UseSqlite($"Data Source={_path}"));
        services.AddScoped<ITaskConfigRepository, TaskConfigRepository>();
        services.AddSingleton(TimeProvider.System);
        var options = new TaskRunnerOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IJobCatalog>(new StaticCatalog(jobs));
        _provider = services.BuildServiceProvider();

        var logger = new RecordingLogger<RecurringJobBootstrapper>();
        var bootstrapper = new RecurringJobBootstrapper(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IJobCatalog>(),
            scheduler,
            Options.Create(options),
            logger);
        return (bootstrapper, logger);
    }

    [Fact]
    public async Task EnabledTask_IsRegisteredWithCronAndQueue()
    {
        var scheduler = new RecordingScheduler();
        var (bootstrapper, _) = Build([Seed.Demo()], scheduler);

        await bootstrapper.StartingAsync(CancellationToken.None);

        var added = Assert.Single(scheduler.Added);
        Assert.Equal("demo-job|*/5 * * * * *|default", added);
    }

    [Fact]
    public async Task DisabledTask_IsRemovedFromScheduler()
    {
        var scheduler = new RecordingScheduler();
        var (bootstrapper, _) = Build([Seed.Demo(enabled: false)], scheduler);

        await bootstrapper.StartingAsync(CancellationToken.None);

        Assert.Empty(scheduler.Added);
        Assert.Equal(["demo-job"], scheduler.Removed);
    }

    [Fact]
    public async Task OrphanConfig_IsCleanedUp_AndLogged()
    {
        var (bootstrapper, logger) = Build([Seed.Demo()], new RecordingScheduler());
        await using (var scope = _provider!.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
            await repository.InitializeAsync(CancellationToken.None);
            await repository.SyncDiscoveredAsync(
            [
                Seed.Demo(),
                new RecurringJobDescriptor("gone-job", "Gone", "*/5 * * * * *", "T, A", typeof(string), null, true)
            ], CancellationToken.None);
        }

        await bootstrapper.StartingAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("gone-job", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UncoveredQueue_LogsError_ButStillRegisters()
    {
        var scheduler = new RecordingScheduler();
        var (bootstrapper, logger) = Build(
            [Seed.Demo(queue: "critical")],
            scheduler,
            o => o.Queues = ["default"]);

        await bootstrapper.StartingAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains("未被订阅的队列", StringComparison.Ordinal));
        Assert.Single(scheduler.Added); // 告警不阻断注册
    }

    [Fact]
    public async Task HeartbeatPingWithoutHeartbeat_LogsWarning()
    {
        var (bootstrapper, logger) = Build(
            [Seed.Demo()],
            new RecordingScheduler(),
            o =>
            {
                o.HeartbeatPingUrl = "https://hc.example/ping";
                o.HeartbeatEnabled = false;
            });

        await bootstrapper.StartingAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("死信告警不会生效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SingleRegistrationFailure_DoesNotBlockOthers()
    {
        var flaky = new FlakyScheduler();
        var (bootstrapper, logger) = Build([Seed.Demo(), Seed.Demo(jobId: "boom-job")], flaky);

        await bootstrapper.StartingAsync(CancellationToken.None);

        Assert.Equal(["demo-job|*/5 * * * * *|default"], flaky.Inner.Added);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains("boom-job", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _provider?.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private sealed class StaticCatalog(IReadOnlyList<RecurringJobDescriptor> jobs) : IJobCatalog
    {
        public IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; } = jobs;
    }

    /// <summary>对指定 JobId 抛异常的调度器，验证单任务注册失败不阻断其余任务。</summary>
    private sealed class FlakyScheduler : ITaskScheduler
    {
        public RecordingScheduler Inner { get; } = new();

        public void AddOrUpdateRecurring(string jobId, string jobType, string cron, string queue)
        {
            if (jobId == "boom-job")
            {
                throw new InvalidOperationException("boom");
            }

            Inner.AddOrUpdateRecurring(jobId, jobType, cron, queue);
        }

        public void RemoveRecurring(string jobId) => Inner.RemoveRecurring(jobId);

        public IReadOnlyList<string> PruneRecurringExcept(IReadOnlyCollection<string> keepJobIds)
            => Inner.PruneRecurringExcept(keepJobIds);

        public string Enqueue(string jobId, string jobType, string queue) => Inner.Enqueue(jobId, jobType, queue);

        public string Schedule(string jobId, string jobType, string queue, DateTimeOffset runAt)
            => Inner.Schedule(jobId, jobType, queue, runAt);
    }
}
