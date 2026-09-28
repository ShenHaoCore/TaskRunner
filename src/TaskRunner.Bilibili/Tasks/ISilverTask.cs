using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface ISilverTask
{
    Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken);
}
