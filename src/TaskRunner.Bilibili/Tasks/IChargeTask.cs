using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface IChargeTask
{
    Task ExecuteAsync(IJobProgress progress, NavData nav, CancellationToken cancellationToken);
}
