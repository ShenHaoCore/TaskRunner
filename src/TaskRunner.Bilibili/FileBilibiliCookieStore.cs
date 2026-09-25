using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Bilibili;

public interface IBilibiliCookieStore
{
    /// <summary>优先读扫码落盘文件，其次配置/环境变量。</summary>
    string GetCookie();

    string FilePath { get; }

    Task SaveAsync(string cookie, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class FileBilibiliCookieStore(
    IOptions<BilibiliOptions> options,
    IHostEnvironment environment,
    ILogger<FileBilibiliCookieStore> logger) : IBilibiliCookieStore
{
    private readonly object _gate = new();

    public string FilePath
    {
        get
        {
            var configured = options.Value.CookieFile?.Trim();
            var solution = AppPaths.FindSolutionRoot(environment.ContentRootPath);
            // 开发：仓库 data/；发布无 .sln：LocalApplicationData，保证 Api/Worker 同机共享。
            var root = solution
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskRunner");

            if (!string.IsNullOrEmpty(configured))
            {
                return Path.IsPathRooted(configured)
                    ? configured
                    : Path.GetFullPath(Path.Combine(root, configured));
            }

            return solution is not null
                ? Path.Combine(root, "data", "bilibili-cookie.txt")
                : Path.Combine(root, "bilibili-cookie.txt");
        }
    }

    public string GetCookie()
    {
        lock (_gate)
        {
            if (File.Exists(FilePath))
            {
                var fromFile = File.ReadAllText(FilePath).Trim();
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    return fromFile;
                }
            }
        }

        return options.Value.Cookie?.Trim() ?? string.Empty;
    }

    public async Task SaveAsync(string cookie, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        var parsed = new BiliCookie(cookie);
        if (!parsed.HasLoginTokens)
        {
            throw new InvalidOperationException("Cookie 缺少 SESSDATA 或 bili_jct。");
        }

        var path = FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, cookie.Trim(), cancellationToken);
        logger.LogInformation("已将 B 站 Cookie 写入 {Path}。", path);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                logger.LogInformation("已删除 B 站 Cookie 文件 {Path}。", FilePath);
            }
        }

        return Task.CompletedTask;
    }
}
