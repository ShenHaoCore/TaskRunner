namespace TaskRunner.Core.Jobs.Progress;

/// <summary>
/// 长任务进度输出抽象。Core 不依赖 Hangfire；Hangfire.Console 仅在调度层适配。
/// </summary>
public interface IJobProgress
{
    void WriteLine(string message);

    void SetProgress(int percent);
}
