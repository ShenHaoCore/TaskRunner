namespace TaskRunner.Core.Common;

public static class JobTypeName
{
    /// <summary>稳定的类型名：FullName + 程序集简单名（不含版本）。</summary>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var fullName = type.FullName ?? type.Name;
        var assemblyName = type.Assembly.GetName().Name ?? type.Assembly.GetName().FullName;
        return $"{fullName}, {assemblyName}";
    }
}
