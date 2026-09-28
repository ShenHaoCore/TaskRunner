using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface ICoinTask
{
    Task ExecuteAsync(IJobProgress progress, NavData nav, DailyTaskInfo info, CancellationToken cancellationToken);
}
