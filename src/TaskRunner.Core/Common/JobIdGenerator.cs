using System.Globalization;
using System.Text;

namespace TaskRunner.Core.Common;

public static class JobIdGenerator
{
    public static string Create(Type jobType, string? explicitId)
    {
        ArgumentNullException.ThrowIfNull(jobType);

        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId.Trim();
        }

        var name = jobType.Name;
        if (name.EndsWith("Job", StringComparison.Ordinal) && name.Length > 3)
        {
            name = name[..^3];
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLower(current, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
