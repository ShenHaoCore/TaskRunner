namespace TaskRunner.Core.Common;

public static class JobTypeResolver
{
    public static Type Resolve(string jobTypeName)
    {
        if (string.IsNullOrWhiteSpace(jobTypeName))
        {
            throw new InvalidOperationException("任务类型为空。");
        }

        var resolved = Type.GetType(jobTypeName, throwOnError: false);
        if (resolved is not null)
        {
            return resolved;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            try
            {
                resolved = assembly.GetType(jobTypeName, throwOnError: false, ignoreCase: false);
            }
            catch (Exception ex) when (ex is ArgumentException or FileLoadException or BadImageFormatException)
            {
                continue;
            }

            if (resolved is not null)
            {
                return resolved;
            }
        }

        throw new InvalidOperationException($"找不到任务类型：{jobTypeName}");
    }
}
