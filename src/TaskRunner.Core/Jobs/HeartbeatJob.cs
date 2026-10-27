using System.Net.Http;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Core.Jobs;

/// <summary>
/// 平台心跳：每分钟一次，成功即证明「调度 → 入队 → 执行 → 状态写回」全链路与存储读写可用。
/// 属平台设施而非业务任务；业务任务仍以插件程序集经 extraAssemblies 注入。
/// </summary>
[RecurringTask("0 * * * * *", JobId = HeartbeatJob.JobIdValue, Name = "平台心跳",
    Description = "每分钟一次的平台心跳；成功代表调度与执行链路及存储读写正常。")]
public sealed class HeartbeatJob(HttpClient client, IOptions<TaskRunnerOptions> options) : IRecurringJob
{
    public const string JobIdValue = "heartbeat";
    public const string HttpClientName = "heartbeat-ping";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // 死信开关（如 healthchecks.io）：配置 HeartbeatPingUrl 后，心跳成功即 ping；
        // ping 失败会让本次作业进入重试/Failed，「心跳没按时到」即触发外部告警。未配置则无事发生。
        var pingUrl = options.Value.HeartbeatPingUrl;
        if (string.IsNullOrWhiteSpace(pingUrl))
        {
            return;
        }

        using var response = await client.PostAsync(pingUrl, content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
