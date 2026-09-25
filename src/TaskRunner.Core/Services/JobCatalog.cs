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

public interface IJobCatalog
{
    IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; }
}

public sealed class JobCatalog : IJobCatalog
{
    public JobCatalog(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
        {
            throw new ArgumentException("至少需要一个程序集。", nameof(assemblies));
        }

        var recurring = new List<RecurringJobDescriptor>();
        foreach (var assembly in assemblies.Distinct())
        {
            recurring.AddRange(RecurringJobScanner.Scan(assembly));
        }

        RecurringJobScanner.EnsureUnique(recurring.Select(job => job.JobId));
        RecurringJobs = recurring.OrderBy(job => job.JobId, StringComparer.Ordinal).ToList();
    }

    public static JobCatalog CreateDefault(params Assembly[] extraAssemblies)
    {
        var assemblies = new List<Assembly> { typeof(IRecurringJob).Assembly };
        if (extraAssemblies is { Length: > 0 })
        {
            assemblies.AddRange(extraAssemblies);
        }

        return new JobCatalog(assemblies.ToArray());
    }

    public IReadOnlyList<RecurringJobDescriptor> RecurringJobs { get; }
}
