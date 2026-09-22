namespace TaskRunner.Core.Jobs;

public interface IRecurringJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
