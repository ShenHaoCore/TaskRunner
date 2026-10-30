using TaskRunner.Core.Models;

namespace TaskRunner.Core.Jobs;

/// <summary>
/// 作业侧运行洞察（引擎无关）：供内置日报类任务读取平台统计与最近失败。
/// 抽象放 Core、实现在引擎装配层（ITaskScheduler 同款模式）——作业零 Hangfire 依赖。
/// </summary>
public interface IJobInsights
{
    /// <summary>平台累计统计快照（成功/失败/排队/处理中/已预约/定时任务/Worker）。</summary>
    JobStatistics GetStatistics();

    /// <summary>最近的失败作业（按时间倒序；失败作业不受成功保留期影响，数据可靠）。</summary>
    IReadOnlyList<RecentExecution> RecentFailures(int limit);
}
