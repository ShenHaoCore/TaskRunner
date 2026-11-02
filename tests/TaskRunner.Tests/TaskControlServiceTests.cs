using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;
using Xunit;

namespace TaskRunner.Tests;

/// <summary>REST 与运行台共用的任务控制用例：校验顺序与副作用（入队/注册/移除）只此一份。</summary>
public sealed class TaskControlServiceTests
{
    private const string JobId = "control-demo";

    private static (TaskControlService Service, RecordingScheduler Scheduler, SqliteConnection Connection) Build(
        bool enabled, string cron = "0 0 * * *", string? queue = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TaskRunnerDbContext>().UseSqlite(connection).Options;
        var db = new TaskRunnerDbContext(options);
        db.Database.EnsureCreated();
        var now = DateTime.UtcNow;
        db.TaskConfigs.Add(new TaskConfig
        {
            JobId = JobId, JobName = JobId, CronExpr = cron, JobType = "Demo",
            Queue = queue ?? string.Empty, IsEnabled = enabled, CreatedAt = now, UpdatedAt = now
        });
        db.SaveChanges();

        var scheduler = new RecordingScheduler();
        return (new TaskControlService(new TaskConfigRepository(db), scheduler), scheduler, connection);
    }

    [Fact]
    public async Task Trigger_WhenMissing_ReturnsNotFound()
    {
        var (service, scheduler, connection) = Build(enabled: true);
        await using var db = connection;

        var result = await service.TriggerAsync("no-such-job", null, CancellationToken.None);

        Assert.Equal(TriggerStatus.NotFound, result.Status);
        Assert.Empty(scheduler.Enqueued);
    }

    [Fact]
    public async Task Trigger_WhenPaused_EnqueuesNothing()
    {
        var (service, scheduler, connection) = Build(enabled: false);
        await using var db = connection;

        var result = await service.TriggerAsync(JobId, null, CancellationToken.None);

        Assert.Equal(TriggerStatus.Paused, result.Status);
        Assert.Empty(scheduler.Enqueued);
        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task Trigger_WhenEnabled_UsesEffectiveQueue()
    {
        var (service, scheduler, connection) = Build(enabled: true, queue: null);
        await using var db = connection;

        var result = await service.TriggerAsync(JobId, null, CancellationToken.None);

        Assert.Equal(TriggerStatus.Accepted, result.Status);
        Assert.False(result.Scheduled);
        Assert.Equal([$"{JobId}|default"], scheduler.Enqueued); // Queue 为空回落 default
        Assert.NotNull(result.QueueJobId);
    }

    [Fact]
    public async Task Resume_WithInvalidCron_ThrowsAndRegistersNothing()
    {
        var (service, scheduler, connection) = Build(enabled: false, cron: "not-a-cron");
        await using var db = connection;

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResumeAsync(JobId, CancellationToken.None));
        Assert.Empty(scheduler.Added); // 脏配置不得静默注册 recurring
    }

    [Fact]
    public async Task Pause_DisablesAndRemovesRecurring()
    {
        var (service, scheduler, connection) = Build(enabled: true);
        await using var db = connection;

        var config = await service.PauseAsync(JobId, CancellationToken.None);

        Assert.NotNull(config);
        Assert.False(config!.IsEnabled);
        Assert.Equal([JobId], scheduler.Removed);
    }
}
