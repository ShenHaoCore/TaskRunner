namespace TaskRunner.Core.Jobs;

public interface IBackgroundJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
