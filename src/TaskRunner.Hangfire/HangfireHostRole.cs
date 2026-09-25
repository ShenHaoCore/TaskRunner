namespace TaskRunner.Hangfire;

public enum HangfireHostRole
{
    /// <summary>仅入队 / Dashboard（生产 Api）。</summary>
    Client,

    /// <summary>仅执行（生产 Worker）。</summary>
    Server,

    /// <summary>入队 + 执行（开发默认，单进程）。</summary>
    Combined
}