namespace TaskRunner.Core.Jobs.Progress;

public sealed class NullJobProgress : IJobProgress
{
    public static NullJobProgress Instance { get; } = new();

    private NullJobProgress()
    {
    }

    public void WriteLine(string message)
    {
    }

    public void SetProgress(int percent)
    {
    }
}
