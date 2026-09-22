using Microsoft.Data.Sqlite;

namespace TaskRunner.Core.Common;

public static class AppPaths
{
    public const string SolutionFileName = "TaskRunner.sln";

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

    public static string ResolveSqliteFile(string configured, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("SQLite 连接字符串为空。");
        }

        if (configured.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Hangfire:Storage 为 Sqlite，但连接字符串仍是 SQL Server 格式。");
        }

        var dataSource = configured;
        if (configured.Contains('='))
        {
            dataSource = new SqliteConnectionStringBuilder(configured).DataSource;
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
