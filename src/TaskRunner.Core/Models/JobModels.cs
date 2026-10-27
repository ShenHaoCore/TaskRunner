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
