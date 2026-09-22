namespace TaskRunner.Core.Jobs.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BackgroundTaskAttribute : Attribute
{
    public string? Description { get; set; }

    public string? JobId { get; set; }

    public string? Name { get; set; }
}
