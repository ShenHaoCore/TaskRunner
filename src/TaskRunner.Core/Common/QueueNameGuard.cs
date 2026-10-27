using System.Text.RegularExpressions;

namespace TaskRunner.Core.Common;

public static partial class QueueNameGuard
{
    public const string Default = "default";

    private static readonly Regex ValidName = HangfireQueueName();

    /// <summary>空/空白 → default；否则校验后返回原串（已 Trim）。</summary>
    public static string Normalize(string? queue)
    {
        if (string.IsNullOrWhiteSpace(queue))
        {
            return Default;
        }

        var trimmed = queue.Trim();
        EnsureValid(trimmed);
        return trimmed;
    }

    public static void EnsureValid(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        if (!ValidName.IsMatch(queue))
        {
            throw new ArgumentException(
                "队列名仅允许小写字母、数字、下划线与连字符（Hangfire 约定）。",
                nameof(queue));
        }
    }

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HangfireQueueName();
}
