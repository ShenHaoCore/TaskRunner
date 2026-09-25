namespace TaskRunner.Api.Contracts;

public sealed record UpdateCronRequest
{
    public required string Cron { get; init; }
}

public sealed record AdminLoginRequest
{
    public required string ApiKey { get; init; }
}

public sealed record QueueJobResult(string JobId, string QueueJobId);
