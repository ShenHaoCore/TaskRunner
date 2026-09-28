using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface IWatchShareTask
{
    Task ExecuteAsync(IJobProgress progress, NavData nav, DailyTaskInfo info, CancellationToken cancellationToken);
}
