namespace TaskRunner.Core.Common;

public sealed class TaskRunnerOptions
{
    public const string SectionName = "Hangfire";

    public string Storage { get; set; } = "SqlServer";

    public string DashboardPath { get; set; } = "/taskrunner";

    public int SchedulePollingIntervalSeconds { get; set; } = 1;

    public int QueuePollIntervalSeconds { get; set; } = 1;

    public int WorkerCount { get; set; } = 5;

    public int RetryAttempts { get; set; } = 3;

    public bool EnableDashboardForms { get; set; } = true;

    public bool ReadOnlyMode { get; set; }

    public string? AdminApiKey { get; set; }

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
