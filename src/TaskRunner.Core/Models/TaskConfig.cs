namespace TaskRunner.Core.Models;

public sealed class TaskConfig
{
    public int Id { get; set; }

    public string JobId { get; set; } = string.Empty;

    public string JobName { get; set; } = string.Empty;

    public string CronExpr { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string JobType { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>目标队列（来自契约标注，同步时覆盖；不可运维 API 修改）。</summary>
    public string Queue { get; set; } = "default";

    /// <summary>生效队列：空白归一为 default。</summary>
    public string EffectiveQueue => string.IsNullOrWhiteSpace(Queue)
        ? Common.QueueNameGuard.Default
        : Queue;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
