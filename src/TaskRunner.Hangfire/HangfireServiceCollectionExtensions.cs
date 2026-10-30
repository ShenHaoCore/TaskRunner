using System.Reflection;
using Hangfire;
using Hangfire.Console;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

public static class HangfireServiceCollectionExtensions
{
    /// <summary>已成功/已删除作业的过期清理扫描间隔。</summary>
    internal const int JobExpirationCheckIntervalMinutes = 5;

    /// <summary>Hangfire Server 心跳间隔（Dashboard 判断 Worker 在线的依据之一）。</summary>
    internal const int ServerHeartbeatIntervalSeconds = 15;

    public static IServiceCollection AddTaskRunnerHangfire(
        this IServiceCollection services,
        IConfiguration configuration,
        HangfireHostRole role)
    {
        var options = configuration.GetSection(TaskRunnerOptions.SectionName).Get<TaskRunnerOptions>() ?? new TaskRunnerOptions();
        var storage = configuration.GetSection(TaskRunnerOptions.SectionName)["Storage"] ?? "PostgreSql";
        if (!storage.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不支持的 Hangfire:Storage={storage}，当前仅支持 PostgreSql。");
        }

        var connection = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:Default。");
        var queuePoll = RequireInterval(options.QueuePollIntervalSeconds, nameof(options.QueuePollIntervalSeconds));
        var schedulePoll = RequireInterval(options.SchedulePollingIntervalSeconds, nameof(options.SchedulePollingIntervalSeconds));
        if (options.RetryAttempts < 0)
        {
            throw new InvalidOperationException("Hangfire:RetryAttempts 不能小于 0。");
        }

        var queues = QueueNameGuard.NormalizeSet(options.Queues);

        // 全局过滤器在此委托内注册（Hangfire 初始化时执行，sp 已可用）：
        // ExclusiveJobFilter 需要注入 ILogger 才能把「互斥静默失效」暴露为可观测告警。
        services.AddHangfire((sp, config) =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    bootstrapper => bootstrapper.UseNpgsqlConnection(connection),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        QueuePollInterval = queuePoll,
                        PrepareSchemaIfNecessary = true,
                        JobExpirationCheckInterval = TimeSpan.FromMinutes(JobExpirationCheckIntervalMinutes)
                    })
                .UseConsole(new ConsoleOptions { FollowJobRetentionPolicy = true });

            ReplaceRetryFilter(options.RetryAttempts);
            var succeededExpiration = TimeSpan.FromMinutes(Math.Max(1, options.SucceededJobExpirationMinutes));
            GlobalJobFilters.Filters.Add(new JobExpirationFilter(succeededExpiration));
            GlobalJobFilters.Filters.Add(new ExclusiveJobFilter(sp.GetRequiredService<ILogger<ExclusiveJobFilter>>()));
        });

        services.AddSingleton<ITaskScheduler, HangfireTaskScheduler>();
        services.AddSingleton<IJobInsights, HangfireJobInsights>();
        services.AddScoped<HangfireJobGateway>();

        if (role is HangfireHostRole.Server or HangfireHostRole.Combined)
        {
            var suffix = role == HangfireHostRole.Combined ? "combined" : "worker";
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = Math.Max(1, options.WorkerCount);
                server.SchedulePollingInterval = schedulePoll;
                server.HeartbeatInterval = TimeSpan.FromSeconds(ServerHeartbeatIntervalSeconds);
                server.Queues = queues;
                server.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:taskrunner-{suffix}";
            });
        }

        return services;
    }

    private static void ReplaceRetryFilter(int attempts)
    {
        var existing = GlobalJobFilters.Filters
            .Select(filter => filter.Instance)
            .OfType<AutomaticRetryAttribute>()
            .Cast<object>()
            .ToList();
        foreach (var filter in existing)
        {
            GlobalJobFilters.Filters.Remove(filter);
        }

        GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute
        {
            Attempts = attempts,
            DelaysInSeconds = RetryBackoff.CalculateDelays(attempts),
            OnAttemptsExceeded = AttemptsExceededAction.Fail,
            LogEvents = true
        });
    }

    private static TimeSpan RequireInterval(int seconds, string name)
    {
        if (seconds < 1)
        {
            throw new InvalidOperationException($"{name} 必须大于等于 1 秒。");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}

/// <summary>重试退避序列：2/4/8…指数翻倍，封顶 1 小时（<see cref="RetryBackoff.MaxDelaySeconds"/>）。</summary>
internal static class RetryBackoff
{
    internal const int MaxDelaySeconds = 3600;

    public static int[] CalculateDelays(int attempts) =>
        Enumerable.Range(0, Math.Max(1, attempts))
            .Select(i => (int)Math.Min(MaxDelaySeconds, Math.Round(2 * Math.Pow(2, i))))
            .ToArray();
}
