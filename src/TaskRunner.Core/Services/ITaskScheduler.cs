namespace TaskRunner.Core.Services;

public interface ITaskScheduler
{
    void AddOrUpdateRecurring(string jobId, string jobType, string cron);

    void RemoveRecurring(string jobId);

    /// <summary>删除 Hangfire 中不在 keepJobIds 内的 recurring（含仅存于 Hangfire、已不在代码/库中的残留）。</summary>
    IReadOnlyList<string> PruneRecurringExcept(IReadOnlyCollection<string> keepJobIds);

    string Enqueue(string jobId, string jobType);
}
