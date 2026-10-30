using Cronos;

namespace TaskRunner.Core.Common;

public static class CronExpressionGuard
{
    public static void EnsureValid(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("Cron 表达式不能为空。", nameof(cron));
        }

        try
        {
            Parse(cron);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException($"Cron 表达式无效：{cron.Trim()}", nameof(cron), ex);
        }
    }

    /// <summary>
    /// 计算未来 N 次执行（UTC）。解析规则与 <see cref="EnsureValid"/> 完全一致
    /// （@宏 / 5 位分钟级 / 6 位含秒），供运行台行内编辑做保存前预览。
    /// 表达式合法但再无下一次（如年份耗尽）时返回不足 count 的结果。
    /// </summary>
    public static IReadOnlyList<DateTime> NextOccurrences(string? cron, int count, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("Cron 表达式不能为空。", nameof(cron));
        }

        count = Math.Clamp(count, 1, 10);
        var expression = Parse(cron);
        var zone = timeZone ?? TimeZoneInfo.Utc;

        var occurrences = new List<DateTime>(count);
        var cursor = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var next = expression.GetNextOccurrence(cursor, zone, inclusive: false);
            if (next is null)
            {
                break;
            }

            occurrences.Add(next.Value);
            cursor = next.Value;
        }

        return occurrences;
    }

    private static CronExpression Parse(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("Cron 表达式不能为空。", nameof(cron));
        }

        var trimmed = cron.Trim();
        if (trimmed.StartsWith('@'))
        {
            return CronExpression.Parse(trimmed);
        }

        var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (5 or 6))
        {
            throw new ArgumentException("Cron 表达式必须是 5 位（分钟级）或 6 位（含秒）。", nameof(cron));
        }

        return CronExpression.Parse(trimmed, parts.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
    }
}
