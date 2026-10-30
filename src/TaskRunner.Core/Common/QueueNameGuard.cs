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

    /// <summary>整组归一化：逐项 Normalize（Trim + 校验）+ 去重保序；空集回退 default。
    /// Worker 订阅队列装配与启动期「队列覆盖校验」共用，避免一侧归一化一侧拿原串对比产生误报。</summary>
    public static string[] NormalizeSet(IEnumerable<string?>? queues)
    {
        var source = queues as string[] ?? queues?.ToArray() ?? [];
        var normalized = new List<string>(source.Length);
        foreach (var item in source)
        {
            var queue = Normalize(item);
            if (!normalized.Contains(queue, StringComparer.Ordinal))
            {
                normalized.Add(queue);
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add(Default);
        }

        return [.. normalized];
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
