using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.PlatformJobs;

/// <summary>
/// 演示模板：模拟长作业，完整展示平台能力——进度条、分阶段 Console 输出、取消响应、失败重试链路。
/// 默认禁用（运行台可启用/手动触发）；接入方抄本类作模板，FailRate 与模拟逻辑按需替换。
/// </summary>
[RecurringTask("0 */5 * * * *", JobId = DemoProgressJob.JobIdValue, Name = "演示模板（进度/取消/重试）",
    Description = "模拟长作业：20 步进度 + 分阶段输出 + 15% 失败演示重试。默认禁用，供接入方抄模板。", Enabled = false)]
public sealed class DemoProgressJob(IJobProgress progress) : IRecurringJob
{
    public const string JobIdValue = "demo-progress";

    /// <summary>模拟失败率：启用本任务即为了看重试链路；业务任务接入时删除。</summary>
    internal const double DefaultFailRate = 0.15;

    /// <summary>运行期失败率（测试可固定为 0 或 1）。</summary>
    internal double FailRate { get; set; } = DefaultFailRate;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        progress.WriteLine("演示作业开始：模拟 20 步数据处理（每步 500 条）");
        for (var step = 1; step <= 20; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken);
            if (step % 5 == 0)
            {
                progress.WriteLine($"阶段 {step / 5}：已处理 {step * 500} 条");
            }

            progress.SetProgress(step * 5);
        }

        progress.WriteLine("数据处理完成，最终校验中…");
        if (Random.Shared.NextDouble() < FailRate)
        {
            // 抛出即进入平台重试链路（2/4/8s 指数退避），Console 详情页可见 ✘ 失败与重试次数
            throw new InvalidOperationException("演示失败：模拟最终校验不通过（观察平台自动重试）");
        }

        progress.SetProgress(100);
        progress.WriteLine("✓ 演示完成：10000 条处理成功");
    }
}
