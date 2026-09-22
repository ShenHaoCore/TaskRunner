namespace TaskRunner.Core.Jobs.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ExclusiveExecutionAttribute : Attribute
{
    public ExclusiveExecutionAttribute(int timeoutSeconds = 60)
    {
        if (timeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "超时必须大于等于 1 秒。");
        }

        TimeoutSeconds = timeoutSeconds;
    }

    public int TimeoutSeconds { get; }
}
