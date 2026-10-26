using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Progress;
using TaskRunner.Core.Models;
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
            options.UseNpgsql(connection, npg => npg.EnableRetryOnFailure(3)));

        IJobCatalog catalog = JobCatalog.CreateDefault(extraJobAssemblies);
        var heartbeatRaw = configuration[$"{TaskRunnerOptions.SectionName}:HeartbeatEnabled"];
        if (bool.TryParse(heartbeatRaw, out var heartbeatEnabled) && !heartbeatEnabled)
        {
            IReadOnlyList<RecurringJobDescriptor> withoutHeartbeat = catalog.RecurringJobs
                .Where(job => !string.Equals(job.JobId, HeartbeatJob.JobIdValue, StringComparison.Ordinal))
                .ToList();
            catalog = new FilteredCatalog(withoutHeartbeat);
        }

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

    private sealed class FilteredCatalog(IReadOnlyList<RecurringJobDescriptor> jobs) : IJobCatalog
    {
        public IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; } = jobs;
    }
}