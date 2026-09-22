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

        var trimmed = cron.Trim();
        try
        {
            if (trimmed.StartsWith('@'))
            {
                CronExpression.Parse(trimmed);
                return;
            }

            var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (5 or 6))
            {
                throw new ArgumentException("Cron 表达式必须是 5 位（分钟级）或 6 位（含秒）。", nameof(cron));
            }

            var format = parts.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            CronExpression.Parse(trimmed, format);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException($"Cron 表达式无效：{trimmed}", nameof(cron), ex);
        }
    }
}
