namespace TaskRunner.Tests;

/// <summary>票 07：运行台 MVC 页冒烟（视图路径与文案；鉴权/trigger 由 REST 与 MVC 缝覆盖）。</summary>
public sealed class RunConsoleSmokeTests
{
    [Fact]
    public void RunView_ExistsWithConsoleCopy()
    {
        var view = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Api", "Views", "Run", "Index.cshtml");
        Assert.True(File.Exists(view), $"找不到运行台视图：{view}");
        var html = File.ReadAllText(view);
        Assert.Contains("任务运行台", html, StringComparison.Ordinal);
        Assert.Contains("立即运行", html, StringComparison.Ordinal);
    }
}
