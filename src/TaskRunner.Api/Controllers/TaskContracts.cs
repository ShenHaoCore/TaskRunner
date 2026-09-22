namespace TaskRunner.Api.Controllers;

public sealed record UpdateCronRequest
{
    public required string Cron { get; init; }
}

public sealed record UpdateParametersRequest
{
    public string? Parameters { get; init; }
}

public sealed record SetReadOnlyRequest
{
    public required bool ReadOnly { get; init; }
}

public sealed record AdminLoginRequest
{
    public required string ApiKey { get; init; }
}

public sealed record BackgroundJobView(string JobId, string JobName, string JobType, string? Description);

public sealed record QueueJobResult(string JobId, string QueueJobId);
