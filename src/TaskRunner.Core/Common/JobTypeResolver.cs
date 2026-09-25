namespace TaskRunner.Core.Common;

public static class JobTypeResolver
{
    public static bool TryResolve(string jobTypeName, out Type? type)
    {
        type = null;
        if (string.IsNullOrWhiteSpace(jobTypeName))
        {
            return false;
        }

        type = Type.GetType(jobTypeName, throwOnError: false);
        if (type is not null)
        {
            return true;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            try
            {
                type = assembly.GetType(jobTypeName, throwOnError: false, ignoreCase: false);
            }
            catch (Exception ex) when (ex is ArgumentException or FileLoadException or BadImageFormatException)
            {
                continue;
            }

            if (type is not null)
            {
                return true;
            }
        }

        type = null;
        return false;
    }

    public static Type Resolve(string jobTypeName)
    {
        if (TryResolve(jobTypeName, out var type) && type is not null)
        {
            return type;
        }

        if (string.IsNullOrWhiteSpace(jobTypeName))
        {
            throw new InvalidOperationException("任务类型为空。");
        }

        throw new InvalidOperationException($"找不到任务类型：{jobTypeName}");
    }
}