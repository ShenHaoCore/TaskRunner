using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Core.Persistence;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

public sealed class DbSyncWindowStoreTests : IDisposable
{
    private readonly string _path;
    private readonly ServiceProvider _provider;

    public DbSyncWindowStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"taskrunner-sync-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<TaskRunnerDbContext>(options => options.UseSqlite($"Data Source={_path}"));
        services.AddSingleton<ISyncWindowStore, DbSyncWindowStore>();
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TaskRunnerDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public void TryBegin_IsIdempotentAcrossCalls()
    {
        var store = _provider.GetRequiredService<ISyncWindowStore>();
        Assert.True(store.TryBegin("20260921120000"));
        Assert.False(store.TryBegin("20260921120000"));
        Assert.Equal(1, store.Count);
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
