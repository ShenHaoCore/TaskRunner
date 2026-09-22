namespace TaskRunner.Core.Services;

public interface ITaskScheduler
{
    void AddOrUpdateRecurring(string jobId, string jobType, string cron);

    void RemoveRecurring(string jobId);

    string EnqueueRecurring(string jobId, string jobType);

    string EnqueueBackground(string jobId, string jobType);
}
