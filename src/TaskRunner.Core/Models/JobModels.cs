namespace TaskRunner.Core.Models;

public sealed record RecurringJobDescriptor(
    string JobId,
    string JobName,
    string Cron,
    string JobType,
    Type ClrType,
    string? Description,
    bool EnabledByDefault,
    string Queue = "default");

public sealed record JobStatistics(
    long Succeeded,
    long Failed,
    long Enqueued,
    long Processing,
    long Scheduled,
    long Recurring,
    long Servers);

public sealed record JobHistoryItem(
    string JobId,
    string State,
    DateTime? OccurredAt,
    string? Reason);

/// <summary>单小时执行计数（Hangfire stats 按小时聚合），用于首页 24h 趋势。</summary>
public sealed record TrendPoint(DateTime Hour, long Succeeded, long Failed);

/// <summary>全站最近一次作业执行（成功/失败），用于首页「最近执行」列表。</summary>
public sealed record RecentExecution(
    string JobId,
    string? JobName,
    string State,
    DateTime? OccurredAt,
    string? Reason,
    long? DurationMs);

public sealed record TaskConfigView(
    string JobId,
    string JobName,
    string CronExpr,
    bool IsEnabled,
    string JobType,
    string? Description,
    string Queue,
    DateTime? LastExecution,
    DateTime? NextExecution,
    string? LastJobState,
    string? Error);
