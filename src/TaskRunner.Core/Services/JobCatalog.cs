using System.Reflection;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Models;

namespace TaskRunner.Core.Services;

public static class RecurringJobScanner
{
    public static IReadOnlyList<RecurringJobDescriptor> Scan(Assembly assembly)
    {
        var jobs = new List<RecurringJobDescriptor>();
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type is not { IsClass: true, IsAbstract: false, IsPublic: true })
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<RecurringTaskAttribute>();
            if (attribute is null)
            {
                continue;
            }

            if (!typeof(IRecurringJob).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{type.FullName} 标记了 RecurringTask，但没有实现 IRecurringJob。");
            }

            CronExpressionGuard.EnsureValid(attribute.Cron);
            var jobId = JobIdGenerator.Create(type, attribute.JobId);
            jobs.Add(new RecurringJobDescriptor(
                jobId,
                string.IsNullOrWhiteSpace(attribute.Name) ? type.Name : attribute.Name.Trim(),
                attribute.Cron,
                JobTypeName.For(type),
                type,
                attribute.Description,
                attribute.Enabled));
        }

        EnsureUnique(jobs.Select(job => job.JobId));
        return jobs.OrderBy(job => job.JobId, StringComparer.Ordinal).ToList();
    }

    internal static void EnsureUnique(IEnumerable<string> jobIds)
    {
        var duplicate = jobIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"任务标识重复：{duplicate.Key}");
        }
    }

    internal static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}

public static class BackgroundJobScanner
{
    public static IReadOnlyList<BackgroundJobDescriptor> Scan(Assembly assembly)
    {
        var jobs = new List<BackgroundJobDescriptor>();
        foreach (var type in RecurringJobScanner.GetLoadableTypes(assembly))
        {
            if (type is not { IsClass: true, IsAbstract: false, IsPublic: true })
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<BackgroundTaskAttribute>();
            if (attribute is null)
            {
                continue;
            }

            if (!typeof(IBackgroundJob).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{type.FullName} 标记了 BackgroundTask，但没有实现 IBackgroundJob。");
            }

            jobs.Add(new BackgroundJobDescriptor(
                JobIdGenerator.Create(type, attribute.JobId),
                string.IsNullOrWhiteSpace(attribute.Name) ? type.Name : attribute.Name.Trim(),
                JobTypeName.For(type),
                type,
                attribute.Description));
        }

        RecurringJobScanner.EnsureUnique(jobs.Select(job => job.JobId));
        return jobs.OrderBy(job => job.JobId, StringComparer.Ordinal).ToList();
    }
}

public interface IJobCatalog
{
    IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; }

    IReadOnlyList<BackgroundJobDescriptor> BackgroundJobs { get; }
}

public sealed class JobCatalog : IJobCatalog
{
    public JobCatalog(Assembly assembly)
    {
        RecurringJobs = RecurringJobScanner.Scan(assembly);
        BackgroundJobs = BackgroundJobScanner.Scan(assembly);
    }

    public static JobCatalog CreateDefault() => new(typeof(IRecurringJob).Assembly);

    public IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; }

    public IReadOnlyList<BackgroundJobDescriptor> BackgroundJobs { get; }
}
