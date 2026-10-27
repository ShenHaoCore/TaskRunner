using Microsoft.Extensions.Configuration;

namespace TaskRunner.Core.Common;

public static class AppPaths
{
    public const string SolutionFileName = "TaskRunner.sln";

    /// <summary>仅用于测试 SQLite 相对路径解析；生产日志不再依赖仓库根。</summary>
    public static string? FindSolutionRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// 将配置中相对的 Serilog File path 解析到 <see cref="AppContext.BaseDirectory"/>/logs，
    /// 避免依赖进程当前目录或仓库里是否存在 .sln。
    /// </summary>
    public static void AbsolutizeSerilogFilePaths(IConfiguration configuration)
    {
        var writeTo = configuration.GetSection("Serilog:WriteTo");
        foreach (var sink in writeTo.GetChildren())
        {
            if (!string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = sink["Args:path"];
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                continue;
            }

            var absolute = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            configuration[$"{sink.Path}:Args:path"] = absolute;
        }
    }

    /// <summary>把形如 <c>Data Source=relative/path.db</c> 的连接串解析为绝对路径；裸路径原样接受。</summary>
    public static string ResolveSqliteFile(string configured, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("SQLite 连接字符串为空。");
        }

        if (configured.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("连接字符串仍是 SQL Server 格式，但当前按 SQLite 解析。");
        }

        var dataSource = configured;
        foreach (var part in configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                dataSource = part["Data Source=".Length..].Trim();
                break;
            }
        }

        if (!Path.IsPathRooted(dataSource))
        {
            var root = FindSolutionRoot(contentRoot) ?? Path.GetFullPath(contentRoot);
            dataSource = Path.Combine(root, dataSource);
        }

        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return dataSource;
    }
}
