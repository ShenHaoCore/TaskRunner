using Hangfire;
using Hangfire.Community.Dashboard.Forms;
using Hangfire.SqlServer;
using Hangfire.Storage.SQLite;
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
        string contentRoot,
        HangfireHostRole role)
    {
        var options = configuration.GetSection(TaskRunnerOptions.SectionName).Get<TaskRunnerOptions>() ?? new TaskRunnerOptions();
        var storage = configuration.GetSection(TaskRunnerOptions.SectionName)["Storage"] ?? "SqlServer";
        var useSqlite = storage.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
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
                .UseRecommendedSerializerSettings();
            ConfigureStorage(config, configuration, contentRoot, queuePoll, useSqlite);

            if (role == HangfireHostRole.Client && options.EnableDashboardForms)
            {
                config.UseManagementPages(typeof(TaskDashboardJobs).Assembly);
            }
        });

        services.AddSingleton<ITaskScheduler, HangfireTaskScheduler>();

        if (role == HangfireHostRole.Server)
        {
            ReplaceRetryFilter(options.RetryAttempts);
            if (!useSqlite)
            {
                GlobalJobFilters.Filters.Add(new ExclusiveJobFilter());
            }

            var workerCount = Math.Max(1, options.WorkerCount);
            if (useSqlite)
            {
                workerCount = 1;
            }

            services.AddHangfireServer(server =>
            {
                server.WorkerCount = workerCount;
                server.SchedulePollingInterval = schedulePoll;
                server.HeartbeatInterval = TimeSpan.FromSeconds(15);
                server.Queues = ["default"];
                server.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:taskrunner-worker";
            });
        }

        return services;
    }

    private static void ConfigureStorage(
        IGlobalConfiguration config,
        IConfiguration configuration,
        string contentRoot,
        TimeSpan queuePoll,
        bool useSqlite)
    {
        var connection = configuration.GetConnectionString("Hangfire")
            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:Hangfire。");

        if (useSqlite)
        {
            var path = AppPaths.ResolveSqliteFile(connection, contentRoot);
            Console.Error.WriteLine($"[TaskRunner] Hangfire 存储=Sqlite，文件={path}。双进程共享此库可能导致 database is locked / 原生崩溃。");
            config.UseSQLiteStorage(path, new SQLiteStorageOptions
            {
                QueuePollInterval = queuePoll,
                InvisibilityTimeout = TimeSpan.FromMinutes(5),
                JournalMode = SQLiteStorageOptions.JournalModes.WAL,
                PoolSize = 1
            });
            return;
        }

        Console.WriteLine($"[TaskRunner] Hangfire 存储=SqlServer，连接={MaskConnection(connection)}");
        config.UseSqlServerStorage(connection, new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
            QueuePollInterval = queuePoll,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = false,
            PrepareSchemaIfNecessary = true,
            SqlClientFactory = SqlClientFactory.Instance
        });
    }

    private static string MaskConnection(string connection)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connection);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return "(connection string)";
        }
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
