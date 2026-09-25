using Hangfire;
using Hangfire.Console;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddTaskRunnerHangfire(
        this IServiceCollection services,
        IConfiguration configuration,
        HangfireHostRole role)
    {
        var options = configuration.GetSection(TaskRunnerOptions.SectionName).Get<TaskRunnerOptions>() ?? new TaskRunnerOptions();
        var storage = configuration.GetSection(TaskRunnerOptions.SectionName)["Storage"] ?? "SqlServer";
        if (!storage.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不支持的 Hangfire:Storage={storage}，当前仅支持 SqlServer。");
        }

        var connection = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:Default。");
        var queuePoll = RequireInterval(options.QueuePollIntervalSeconds, nameof(options.QueuePollIntervalSeconds));
        var schedulePoll = RequireInterval(options.SchedulePollingIntervalSeconds, nameof(options.SchedulePollingIntervalSeconds));
        if (options.RetryAttempts < 0)
        {
            throw new InvalidOperationException("Hangfire:RetryAttempts 不能小于 0。");
        }

        services.AddHangfire((_, config) =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connection, new SqlServerStorageOptions
                {
                    CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    QueuePollInterval = queuePoll,
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = false,
                    PrepareSchemaIfNecessary = true,
                    JobExpirationCheckInterval = TimeSpan.FromMinutes(5),
                    SqlClientFactory = SqlClientFactory.Instance
                })
                .UseConsole(new ConsoleOptions { FollowJobRetentionPolicy = true });
        });

        services.AddSingleton<ITaskScheduler, HangfireTaskScheduler>();
        services.AddScoped<HangfireJobGateway>();

        if (role is HangfireHostRole.Server or HangfireHostRole.Combined)
        {
            ReplaceRetryFilter(options.RetryAttempts);
            var succeededExpiration = TimeSpan.FromMinutes(Math.Max(1, options.SucceededJobExpirationMinutes));
            GlobalJobFilters.Filters.Add(new JobExpirationFilter(succeededExpiration));
            GlobalJobFilters.Filters.Add(new ExclusiveJobFilter());

            var suffix = role == HangfireHostRole.Combined ? "combined" : "worker";
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = Math.Max(1, options.WorkerCount);
                server.SchedulePollingInterval = schedulePoll;
                server.HeartbeatInterval = TimeSpan.FromSeconds(15);
                server.Queues = ["default"];
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
            DelaysInSeconds = [2, 4, 8],
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
