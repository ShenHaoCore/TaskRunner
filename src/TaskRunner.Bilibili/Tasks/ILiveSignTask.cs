using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface ILiveSignTask
{
    Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken);
}
