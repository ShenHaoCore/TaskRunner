using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;

namespace TaskRunner.Core.Services;

public interface ISyncWindowStore
{
    bool TryBegin(string windowKey);

    void Forget(string windowKey);

    int RemoveOlderThan(DateTimeOffset threshold);

    int Count { get; }
}

/// <summary>
/// 基于数据库唯一约束的跨进程幂等窗口。
/// </summary>
public sealed class DbSyncWindowStore(IServiceScopeFactory scopeFactory) : ISyncWindowStore
{
    public int Count
    {
        get
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
            return db.SyncWindows.AsNoTracking().Count();
        }
    }

    public bool TryBegin(string windowKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
        db.SyncWindows.Add(new SyncWindow
        {
            WindowKey = windowKey,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            db.SaveChanges();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public void Forget(string windowKey)
    {
        if (string.IsNullOrWhiteSpace(windowKey))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
        var row = db.SyncWindows.Find(windowKey);
        if (row is null)
        {
            return;
        }

        db.SyncWindows.Remove(row);
        db.SaveChanges();
    }

    public int RemoveOlderThan(DateTimeOffset threshold)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>();
        var cutoff = threshold.UtcDateTime;
        var rows = db.SyncWindows.Where(item => item.CreatedAt < cutoff).ToList();
        if (rows.Count == 0)
        {
            return 0;
        }

        db.SyncWindows.RemoveRange(rows);
        db.SaveChanges();
        return rows.Count;
    }
}

/// <summary>仅用于单元测试的进程内实现。</summary>
public sealed class MemorySyncWindowStore : ISyncWindowStore
{
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);

    public int Count => _seen.Count;

    public bool TryBegin(string windowKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        return _seen.TryAdd(windowKey, 0);
    }

    public void Forget(string windowKey)
    {
        if (!string.IsNullOrWhiteSpace(windowKey))
        {
            _seen.TryRemove(windowKey, out _);
        }
    }

    public int RemoveOlderThan(DateTimeOffset threshold)
    {
        var removed = 0;
        foreach (var key in _seen.Keys)
        {
            if (!DateTime.TryParseExact(key, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var timestamp = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
            if (timestamp >= threshold)
            {
                continue;
            }

            if (_seen.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
