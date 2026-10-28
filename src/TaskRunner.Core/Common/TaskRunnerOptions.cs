namespace TaskRunner.Core.Common;

public sealed class TaskRunnerOptions
{
    public const string SectionName = "Hangfire";

    public string Storage { get; set; } = "PostgreSql";

    public string DashboardPath { get; set; } = "/taskrunner";

    public int SchedulePollingIntervalSeconds { get; set; } = 1;

    public int QueuePollIntervalSeconds { get; set; } = 1;

    public int WorkerCount { get; set; } = 5;

    /// <summary>Hangfire Server 订阅的队列列表（顺序即拉取优先）；缺省仅 default。</summary>
    public string[] Queues { get; set; } = [QueueNameGuard.Default];

    public int RetryAttempts { get; set; } = 3;

    /// <summary>配置级只读；修改需改配置并重启。</summary>
    public bool ReadOnlyMode { get; set; }

    public string? AdminApiKey { get; set; }

    /// <summary>交互式登录账号（MVC /login 表单）。缺省 admin；修改需重启。</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>交互式登录密码（MVC /login 表单）。生产未配置时启动记录警告、登录页给出引导；
    /// 机器调用仍走 AdminApiKey（Header / Basic），两者互不影响。修改需重启。</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Hangfire 已成功/已删除作业的保留分钟数（默认 60）。</summary>
    public int SucceededJobExpirationMinutes { get; set; } = 60;

    /// <summary>平台心跳任务（heartbeat，每分钟）开关；修改需改配置并重启。</summary>
    public bool HeartbeatEnabled { get; set; } = true;

    /// <summary>管理员 Cookie 的 Secure 标记；TLS 反代部署时置 true，默认 false 兼容本机 http。修改需重启。</summary>
    public bool CookieSecure { get; set; }

    /// <summary>心跳死信开关的 ping 地址（如 healthchecks.io 的 check URL）。配置后心跳成功即 POST ping；
    /// ping 失败计入作业重试/Failed，外部服务以「未按时收到 ping」告警。留空则不 ping。修改需重启。</summary>
    public string? HeartbeatPingUrl { get; set; }

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