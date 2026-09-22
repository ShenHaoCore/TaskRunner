namespace TaskRunner.Core.Models;

public sealed class SyncWindow
{
    public string WindowKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public sealed class RuntimeSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
