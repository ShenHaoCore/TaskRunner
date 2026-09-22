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

    Task<TaskConfig?> UpdateParametersAsync(string jobId, string? parameters, CancellationToken cancellationToken);
}

public sealed class TaskConfigRepository(TaskRunnerDbContext db) : ITaskConfigRepository
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 迁移按 SQL Server 生成；SQLite（测试/可选开发）用 EnsureCreated，避免跨提供程序快照告警。
        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        await db.Database.MigrateAsync(cancellationToken);
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
                    Parameters = null,
                    Description = job.Description,
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

    public async Task<TaskConfig?> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken)
    {
        var row = await FindAsync(jobId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        row.IsEnabled = enabled;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<TaskConfig?> UpdateCronAsync(string jobId, string cron, CancellationToken cancellationToken)
    {
        var row = await FindAsync(jobId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        row.CronExpr = cron.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<TaskConfig?> UpdateParametersAsync(string jobId, string? parameters, CancellationToken cancellationToken)
    {
        var row = await FindAsync(jobId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        row.Parameters = string.IsNullOrWhiteSpace(parameters) ? null : parameters.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }
}
