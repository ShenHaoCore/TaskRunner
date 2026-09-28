using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

public interface IVipPrivilegeTask
{
    Task ExecuteAsync(IJobProgress progress, NavData nav, CancellationToken cancellationToken);
}
