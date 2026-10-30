using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Persistence;

namespace TaskRunner.PlatformJobs;

/// <summary>
/// 平台自检：运行期持续体检（与启动期 Bootstrapper 校验互补）。任一项异常只告警不抛——
/// 巡检任务自身不应因被巡检对象的问题而进入重试/Failed。
/// </summary>
[RecurringTask("0 0 6 * * *", JobId = PlatformSelfCheckJob.JobIdValue, Name = "平台自检",
    Description = "每天 06:00 巡检：业务库连通、日志目录可写、心跳组合配置、暂停任务概况。只告警不失败。")]
public sealed class PlatformSelfCheckJob(
    TaskRunnerDbContext db,
    IConfiguration configuration,
    IOptions<TaskRunnerOptions> options,
    IJobProgress progress,
    ILogger<PlatformSelfCheckJob> logger) : IRecurringJob
{
    public const string JobIdValue = "self-check";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var issues = 0;
        progress.WriteLine("── 平台自检 ──");

        // 1. 业务库连通与概况
        try
        {
            var taskCount = await db.TaskConfigs.CountAsync(cancellationToken);
            var paused = await db.TaskConfigs
                .Where(config => !config.IsEnabled)
                .Select(config => config.JobId)
                .ToListAsync(cancellationToken);
            progress.WriteLine($"✓ 业务库连通：TaskConfigs {taskCount} 行，暂停 {paused.Count} 个");
            if (paused.Count > 0)
            {
                progress.WriteLine($"  ○ 暂停中：{string.Join("、", paused)}");
            }
        }
        catch (Exception ex)
        {
            issues++;
            progress.WriteLine($"✗ 业务库不可达：{ex.Message}");
        }

        progress.SetProgress(30);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. 日志目录可写性
        foreach (var directory in LogDirectories.Resolve(configuration))
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, $".self-check-{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(probe, "probe", cancellationToken);
                File.Delete(probe);
                progress.WriteLine($"✓ 日志目录可写：{directory}");
            }
            catch (Exception ex)
            {
                issues++;
                progress.WriteLine($"✗ 日志目录不可写：{directory}（{ex.Message}）");
            }
        }

        progress.SetProgress(60);
        cancellationToken.ThrowIfCancellationRequested();

        // 3. 心跳组合配置（对齐启动期 WarnHeartbeatCombo，运行期持续提醒）
        var heartbeat = options.Value;
        if (!string.IsNullOrWhiteSpace(heartbeat.HeartbeatPingUrl) && !heartbeat.HeartbeatEnabled)
        {
            issues++;
            progress.WriteLine("✗ 已配置 HeartbeatPingUrl 但 HeartbeatEnabled=false：死信告警不会生效");
        }
        else
        {
            progress.WriteLine(heartbeat.HeartbeatEnabled ? "✓ 心跳已启用" : "✓ 心跳已关闭（未配置死信 ping，无组合冲突）");
        }

        progress.SetProgress(100);
        progress.WriteLine(issues == 0 ? "── 自检通过 ──" : $"── 自检完成：{issues} 项异常 ──");
        logger.LogInformation("平台自检完成：异常 {Issues} 项。", issues);
    }
}
