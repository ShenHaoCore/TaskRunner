namespace TaskRunner.Api.Contracts;

public sealed record UpdateCronRequest
{
    public required string Cron { get; init; }
}

public sealed record AdminLoginRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }
}

public sealed record QueueJobResult(string JobId, string QueueJobId, bool Scheduled = false);

/// <summary>触发请求：省略或空体=立即；runAt 与 delaySeconds 互斥。</summary>
public sealed record TriggerRequest
{
    public DateTimeOffset? RunAt { get; init; }

    public int? DelaySeconds { get; init; }
}

/// <summary>Cron 下次执行预览（UTC ISO 时间，最多 5 条）。</summary>
public sealed record CronPreviewView(IReadOnlyList<DateTime> Occurrences);
