namespace TaskRunner.Core.Models;

public sealed class TaskConfig
{
    public int Id { get; set; }

    public string JobId { get; set; } = string.Empty;

    public string JobName { get; set; } = string.Empty;

    public string CronExpr { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string JobType { get; set; } = string.Empty;

    public string? Parameters { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
