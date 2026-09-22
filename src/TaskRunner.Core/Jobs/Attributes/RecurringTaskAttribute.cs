namespace TaskRunner.Core.Jobs.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RecurringTaskAttribute : Attribute
{
    public RecurringTaskAttribute(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("Cron 表达式不能为空。", nameof(cron));
        }

        Cron = cron.Trim();
    }

    public string Cron { get; }

    public string? Description { get; set; }

    public string? JobId { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;
}
