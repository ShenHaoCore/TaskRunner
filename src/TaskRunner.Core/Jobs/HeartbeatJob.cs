using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Core.Jobs;

/// <summary>
/// 平台心跳：每分钟一次，成功即证明「调度 → 入队 → 执行 → 状态写回」全链路与存储读写可用。
/// 属平台设施而非业务任务；业务任务仍以插件程序集经 extraAssemblies 注入。
/// </summary>
[RecurringTask("0 * * * * *", JobId = HeartbeatJob.JobIdValue, Name = "平台心跳",
    Description = "每分钟一次的平台心跳；成功代表调度与执行链路及存储读写正常。")]
public sealed class HeartbeatJob : IRecurringJob
{
    public const string JobIdValue = "heartbeat";

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
