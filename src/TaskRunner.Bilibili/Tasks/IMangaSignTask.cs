using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface IMangaSignTask
{
    Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken);
}
