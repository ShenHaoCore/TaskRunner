using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;

namespace TaskRunner.Core.Services;

public interface ITaskConfigRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task SyncDiscoveredAsync(IReadOnlyCollection<RecurringJobDescriptor> jobs, CancellationToken cancellationToken);

    /// <summary>删除程序集中已不存在的配置行，返回被删除的 JobId。</summary>
    Task<IReadOnlyList<string>> RemoveMissingAsync(IReadOnlyCollection<string> discoveredJobIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskConfig>> ListAsync(CancellationToken cancellationToken);

    Task<TaskConfig?> FindAsync(string jobId, CancellationToken cancellationToken);

    Task<TaskConfig?> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken);

    Task<TaskConfig?> UpdateCronAsync(string jobId, string cron, CancellationToken cancellationToken);
}

public sealed class TaskConfigRepository(TaskRunnerDbContext db) : ITaskConfigRepository
{
    private const int MigrateAdvisoryLockId = 738231001;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 生产 / 开发：PostgreSQL 走 EF 迁移。测试：SQLite 与迁移模型有提供程序差异，改 EnsureCreated。
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        // Api/Worker 双进程（或 Api 多副本）同时首启：迁移以会话级 advisory lock 串行化，
        // 避免 __EFMigrationsHistory 与 CREATE TABLE 并发冲突。
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_lock({MigrateAdvisoryLockId});", cancellationToken);
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_unlock({MigrateAdvisoryLockId});", cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task SyncDiscoveredAsync(IReadOnlyCollection<RecurringJobDescriptor> jobs, CancellationToken cancellationToken)
    {
        var existing = await db.TaskConfigs.ToListAsync(cancellationToken);
        var map = existing.ToDictionary(item => item.JobId, StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var job in jobs)
        {
            if (!map.TryGetValue(job.JobId, out var row))
            {
                db.TaskConfigs.Add(new TaskConfig
                {
                    JobId = job.JobId,
                    JobName = job.JobName,
                    CronExpr = job.Cron,
                    IsEnabled = job.EnabledByDefault,
                    JobType = job.JobType,
                    Description = job.Description,
                    Queue = job.Queue,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            var changed = false;
            if (!string.Equals(row.JobName, job.JobName, StringComparison.Ordinal))
            {
                row.JobName = job.JobName;
                changed = true;
            }

            if (!string.Equals(row.JobType, job.JobType, StringComparison.Ordinal))
            {
                row.JobType = job.JobType;
                changed = true;
            }

            if (!string.Equals(row.Description, job.Description, StringComparison.Ordinal))
            {
                row.Description = job.Description;
                changed = true;
            }

            if (!string.Equals(row.Queue, job.Queue, StringComparison.Ordinal))
            {
                row.Queue = job.Queue;
                changed = true;
            }

            // 代码默认启用、且库中仍为禁用且从未被运维改过（CreatedAt==UpdatedAt）时，纠正为启用并注册到 Hangfire。
            if (job.EnabledByDefault && !row.IsEnabled && row.CreatedAt == row.UpdatedAt)
            {
                row.IsEnabled = true;
                changed = true;
            }

            if (changed)
            {
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> RemoveMissingAsync(
        IReadOnlyCollection<string> discoveredJobIds,
        CancellationToken cancellationToken)
    {
        // 空发现集（如插件程序集整体加载失败）绝不触发删除——那是配置大扫除，不是同步。
        if (discoveredJobIds.Count == 0)
        {
            return [];
        }

        var discovered = discoveredJobIds.ToHashSet(StringComparer.Ordinal);
        var all = await db.TaskConfigs.ToListAsync(cancellationToken);
        var orphans = all.Where(item => !discovered.Contains(item.JobId)).ToList();
        if (orphans.Count == 0)
        {
            return [];
        }

        var ids = orphans.Select(item => item.JobId).ToList();
        db.TaskConfigs.RemoveRange(orphans);
        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<IReadOnlyList<TaskConfig>> ListAsync(CancellationToken cancellationToken)
        => await db.TaskConfigs.OrderBy(item => item.JobId).ToListAsync(cancellationToken);

    public Task<TaskConfig?> FindAsync(string jobId, CancellationToken cancellationToken)
        => db.TaskConfigs.FirstOrDefaultAsync(item => item.JobId == jobId, cancellationToken);

    public Task<TaskConfig?> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken)
        => UpdateRowAsync(jobId, row => row.IsEnabled = enabled, cancellationToken);

    public Task<TaskConfig?> UpdateCronAsync(string jobId, string cron, CancellationToken cancellationToken)
        => UpdateRowAsync(jobId, row => row.CronExpr = cron.Trim(), cancellationToken);

    /// <summary>
    /// 读-改-写统一入口：xmin 并发冲突（并发启停/Cron 更新）时重载行并重试一次，
    /// 第二次仍冲突则抛出（调用方按 500 处理），不再静默丢失更新。
    /// </summary>
    private async Task<TaskConfig?> UpdateRowAsync(
        string jobId, Action<TaskConfig> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = await FindAsync(jobId, cancellationToken);
            if (row is null)
            {
                return null;
            }

            mutate(row);
            row.UpdatedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return row;
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                await db.Entry(row).ReloadAsync(cancellationToken);
            }
        }
    }
}
