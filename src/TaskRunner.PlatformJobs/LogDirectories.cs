using Microsoft.Extensions.Configuration;

namespace TaskRunner.PlatformJobs;

/// <summary>
/// 日志目录解析：与启动期 AppPaths.AbsolutizeSerilogFilePaths 同规则——
/// 取 Serilog File sink 的 Args:path（相对路径归一到 BaseDirectory），无配置回退 BaseDirectory/logs。
/// </summary>
internal static class LogDirectories
{
    public static IReadOnlyList<string> Resolve(IConfiguration configuration)
    {
        var directories = new List<string>();
        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            if (!string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = sink["Args:path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var absolute = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory) && !directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(directory);
            }
        }

        if (directories.Count == 0)
        {
            directories.Add(Path.Combine(AppContext.BaseDirectory, "logs"));
        }

        return directories;
    }
}
