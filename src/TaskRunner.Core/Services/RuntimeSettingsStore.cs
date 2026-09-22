using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Persistence;

namespace TaskRunner.Core.Services;

public interface IRuntimeSettingsStore
{
    Task<bool> GetReadOnlyModeAsync(CancellationToken cancellationToken);

    Task SetReadOnlyModeAsync(bool readOnly, CancellationToken cancellationToken);

    Task SyncFromConfigurationAsync(CancellationToken cancellationToken);
}

public sealed class RuntimeSettingsStore(
    TaskRunnerDbContext db,
    IOptions<TaskRunnerOptions> options) : IRuntimeSettingsStore
{
    public const string ReadOnlyKey = "ReadOnlyMode";

    public async Task<bool> GetReadOnlyModeAsync(CancellationToken cancellationToken)
    {
        var row = await db.RuntimeSettings.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Key == ReadOnlyKey, cancellationToken);
        if (row is null)
        {
            return options.Value.ReadOnlyMode;
        }

        return bool.TryParse(row.Value, out var value) && value;
    }

    public async Task SetReadOnlyModeAsync(bool readOnly, CancellationToken cancellationToken)
    {
        var row = await db.RuntimeSettings.FirstOrDefaultAsync(item => item.Key == ReadOnlyKey, cancellationToken);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            db.RuntimeSettings.Add(new RuntimeSetting
            {
                Key = ReadOnlyKey,
                Value = readOnly ? bool.TrueString : bool.FalseString,
                UpdatedAt = now
            });
        }
        else
        {
            row.Value = readOnly ? bool.TrueString : bool.FalseString;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncFromConfigurationAsync(CancellationToken cancellationToken)
    {
        var exists = await db.RuntimeSettings.AsNoTracking()
            .AnyAsync(item => item.Key == ReadOnlyKey, cancellationToken);
        if (!exists)
        {
            await SetReadOnlyModeAsync(options.Value.ReadOnlyMode, cancellationToken);
        }
    }
}
