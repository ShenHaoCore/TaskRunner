namespace TaskRunner.Worker.Services;

public sealed class WorkerHeartbeatService(ILogger<WorkerHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker 心跳服务已启动，机器 {Machine}。", Environment.MachineName);
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Worker 心跳：{Machine} {Time:O}", Environment.MachineName, DateTimeOffset.UtcNow);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
