using Hangfire;
using Hangfire.Console;
using Hangfire.PostgreSql;
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

        var queues = NormalizeQueues(options.Queues);

        services.AddHangfire((_, config) =>
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
                        JobExpirationCheckInterval = TimeSpan.FromMinutes(5)
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
                server.Queues = queues;
                server.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:taskrunner-{suffix}";
            });
        }

        return services;
    }

    private static string[] NormalizeQueues(string[]? configured)
    {
        var source = configured is { Length: > 0 } ? configured : [QueueNameGuard.Default];
        var normalized = new List<string>(source.Length);
        foreach (var item in source)
        {
            var queue = QueueNameGuard.Normalize(item);
            if (!normalized.Contains(queue, StringComparer.Ordinal))
            {
                normalized.Add(queue);
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add(QueueNameGuard.Default);
        }

        return normalized.ToArray();
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
            DelaysInSeconds = Enumerable.Range(0, Math.Max(1, attempts))
                .Select(i => (int)Math.Min(3600, Math.Round(2 * Math.Pow(2, i))))
                .ToArray(),
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
