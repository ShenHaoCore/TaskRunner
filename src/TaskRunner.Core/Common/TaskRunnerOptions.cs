namespace TaskRunner.Core.Common;

public sealed class TaskRunnerOptions
{
    public const string SectionName = "Hangfire";

    public string Storage { get; set; } = "PostgreSql";

    public string DashboardPath { get; set; } = "/taskrunner";

    public int SchedulePollingIntervalSeconds { get; set; } = 1;

    public int QueuePollIntervalSeconds { get; set; } = 1;

    public int WorkerCount { get; set; } = 5;

    public int RetryAttempts { get; set; } = 3;

    /// <summary>配置级只读；修改需改配置并重启。</summary>
    public bool ReadOnlyMode { get; set; }

    public string? AdminApiKey { get; set; }

    /// <summary>Hangfire 已成功/已删除作业的保留分钟数（默认 60）。</summary>
    public int SucceededJobExpirationMinutes { get; set; } = 60;

    /// <summary>平台心跳任务（heartbeat，每分钟）开关；修改需改配置并重启。</summary>
    public bool HeartbeatEnabled { get; set; } = true;

    public string NormalizeDashboardPath()
    {
        var path = string.IsNullOrWhiteSpace(DashboardPath) ? "/taskrunner" : DashboardPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return path;
    }
}