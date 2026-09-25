using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;

namespace TaskRunner.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddTaskRunnerCore(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] extraJobAssemblies)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<TaskRunnerOptions>(configuration.GetSection(TaskRunnerOptions.SectionName));

        var connection = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:Default。");

        services.AddDbContext<TaskRunnerDbContext>(options =>
            options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3)));

        var catalog = JobCatalog.CreateDefault(extraJobAssemblies);
        services.AddSingleton<IJobCatalog>(catalog);
        services.AddScoped<ITaskConfigRepository, TaskConfigRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<JobExecutor>();
        services.AddScoped<JobProgressContext>();
        services.AddTransient<IJobProgress>(sp => sp.GetRequiredService<JobProgressContext>().Current);

        foreach (var type in catalog.RecurringJobs.Select(job => job.ClrType).Distinct())
        {
            services.AddScoped(type);
        }

        return services;
    }
}