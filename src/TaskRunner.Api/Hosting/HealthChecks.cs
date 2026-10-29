using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskRunner.Core.Persistence;

namespace TaskRunner.Api.Hosting;

/// <summary>业务库连通性检查（EF 迁移库可达即可写）。</summary>
public sealed class TaskRunnerDbHealthCheck(TaskRunnerDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("业务库可达。")
                : HealthCheckResult.Degraded("业务库无法连接。");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("业务库健康检查失败。", ex);
        }
    }
}

/// <summary>Hangfire 存储检查（监控 API 可读即视为健康）。</summary>
public sealed class HangfireStorageHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _ = storage.GetMonitoringApi().GetStatistics();
            return Task.FromResult(HealthCheckResult.Healthy("Hangfire 存储可达。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire 存储健康检查失败。", ex));
        }
    }
}
