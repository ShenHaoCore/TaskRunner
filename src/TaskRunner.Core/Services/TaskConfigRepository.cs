using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;

namespace TaskRunner.Core.Services;

public interface ITaskConfigRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 在会话级 advisory lock 内执行启动编排（迁移 → 同步 → 清理）。Api/Worker 双进程并发首启时
    /// 整段串行化，避免两侧同时读空表再插入相同 JobId 撞唯一索引导致宿主启动失败。
    /// SQLite（测试）无 advisory lock，直接执行。
    /// </summary>
    Task RunUnderBootstrapLockAsync(Func<Task> body, CancellationToken cancellationToken);

    /// <summary>同步发现到的任务定义，返回同步后的完整配置列表（含新增行，避免调用方重复全量读取）。</summary>
    Task<IReadOnlyList<TaskConfig>> SyncDiscoveredAsync(IReadOnlyCollection<RecurringJobDescriptor> jobs, CancellationToken cancellationToken);

    /// <summary>删除程序集中已不存在的配置行，返回被删除的 JobId。</summary>
    Task<IReadOnlyList<string>> RemoveMissingAsync(IReadOnlyCollection<string> discoveredJobIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskConfig>> ListAsync(CancellationToken cancellationToken);

    Task<TaskConfig?> FindAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>基于已加载实体做读-改-写（xmin 冲突自动重载重试），调用方无需再按 JobId 重查一次。</summary>
    Task<TaskConfig?> UpdateAsync(TaskConfig row, Action<TaskConfig> mutate, CancellationToken cancellationToken);

    Task<TaskConfig?> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken);

    Task<TaskConfig?> UpdateCronAsync(string jobId, string cron, CancellationToken cancellationToken);
}

public sealed class TaskConfigRepository(TaskRunnerDbContext db) : ITaskConfigRepository
{
    private const int MigrateAdvisoryLockId = 738231001;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 生产 / 开发：PostgreSQL 走 EF 迁移。测试：SQLite 与迁移模型有提供程序差异，改 EnsureCreated。
        // 迁移本身由 RunUnderBootstrapLockAsync 的会话级 advisory lock 串行化（Api/Worker 并发首启）。
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    public async Task RunUnderBootstrapLockAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        // 测试：SQLite 无 advisory lock，直接执行。
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            await body();
            return;
        }

        // 会话级锁与后续 EF 查询共用这条显式打开的连接（同一会话），锁期间整个编排串行化。
        await db.Database.OpenConnectionAsync(cancellationToken);
        var previousTimeout = db.Database.GetCommandTimeout();
        try
        {
            // 等锁命令可能超过默认 30s CommandTimeout（另一进程正持锁跑重量级迁移）——
            // 置 0 表示等到获得为止，与「启动编排串行化」的设计预期一致；退出时恢复原值。
            db.Database.SetCommandTimeout(0);
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_lock({MigrateAdvisoryLockId});", cancellationToken);
            try
            {
                await body();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_unlock({MigrateAdvisoryLockId});", cancellationToken);
            }
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyList<TaskConfig>> SyncDiscoveredAsync(IReadOnlyCollection<RecurringJobDescriptor> jobs, CancellationToken cancellationToken)
    {
        var existing = await db.TaskConfigs.ToListAsync(cancellationToken);
        var map = existing.ToDictionary(item => item.JobId, StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = new List<TaskConfig>();

        foreach (var job in jobs)
        {
            if (!map.TryGetValue(job.JobId, out var row))
            {
                var created = new TaskConfig
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
                };
                db.TaskConfigs.Add(created);
                added.Add(created);
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

        // SaveChanges 后主键已生成，直接拼接返回，调用方无需再全量查询一次。
        return existing.Concat(added).OrderBy(item => item.JobId, StringComparer.Ordinal).ToList();
    }

    /// <summary>发现集缺失任务的删除宽限（分钟）：滚动升级期间 Api/Worker 版本短暂不一致，
    /// 旧进程的发现集不含新任务——立即删除会误删新版本刚注册的任务并摘除其 recurring。</summary>
    public const int OrphanGraceMinutes = 10;

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
        var graceDeadline = DateTime.UtcNow - TimeSpan.FromMinutes(OrphanGraceMinutes);
        var all = await db.TaskConfigs.ToListAsync(cancellationToken);
        var orphans = all
            .Where(item => !discovered.Contains(item.JobId) && item.UpdatedAt < graceDeadline)
            .ToList();
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

    /// <inheritdoc />
    public async Task<TaskConfig?> UpdateAsync(TaskConfig row, Action<TaskConfig> mutate, CancellationToken cancellationToken)
    {
        mutate(row);
        row.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return row;
        }
        catch (DbUpdateConcurrencyException)
        {
            // 与 UpdateRowAsync 同一套重载重试通道：重载最新行后按标准路径重试一次。
            await db.Entry(row).ReloadAsync(cancellationToken);
            return await UpdateRowAsync(row.JobId, mutate, cancellationToken);
        }
    }

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
