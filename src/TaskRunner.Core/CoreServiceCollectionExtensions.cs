using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;

namespace TaskRunner.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddTaskRunnerCore(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<TaskRunnerOptions>(configuration.GetSection(TaskRunnerOptions.SectionName));

        var storage = configuration.GetSection(TaskRunnerOptions.SectionName)["Storage"] ?? "SqlServer";
        var connection = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:Default。");

        services.AddDbContext<TaskRunnerDbContext>(options =>
        {
            if (storage.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var path = AppPaths.ResolveSqliteFile(connection, contentRoot);
                options.UseSqlite($"Data Source={path}");
                return;
            }

            options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3));
        });

        var catalog = JobCatalog.CreateDefault();
        services.AddSingleton<IJobCatalog>(catalog);
        services.AddScoped<ITaskConfigRepository, TaskConfigRepository>();
        services.AddScoped<IRuntimeSettingsStore, RuntimeSettingsStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISyncWindowStore, DbSyncWindowStore>();
        services.AddScoped<JobExecutor>();

        foreach (var type in catalog.RecurringJobs.Select(job => job.ClrType)
                     .Concat(catalog.BackgroundJobs.Select(job => job.ClrType))
                     .Distinct())
        {
            services.AddScoped(type);
        }

        services.AddHostedService<TaskConfigDatabaseInitializer>();
        services.AddHostedService<RuntimeSettingsBootstrapper>();
        return services;
    }
}
