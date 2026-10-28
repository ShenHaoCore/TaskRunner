namespace TaskRunner.Tests;

/// <summary>票 07：运行台 MVC 页冒烟（路径与文案；鉴权/trigger 由 REST 缝覆盖）。</summary>
public sealed class RunConsoleSmokeTests
{
    [Fact]
    public void RunView_ExistsWithConsoleCopy()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "TaskRunner.Api"));
        var view = Path.Combine(root, "Views", "Home", "Run.cshtml");
        var script = Path.Combine(root, "wwwroot", "js", "run.js");
        Assert.True(File.Exists(view), $"找不到运行台视图：{view}");
        Assert.True(File.Exists(script), $"找不到运行台脚本：{script}");
        var html = File.ReadAllText(view);
        var js = File.ReadAllText(script);
        Assert.Contains("任务运行台", html, StringComparison.Ordinal);
        Assert.Contains("/api/tasks", js, StringComparison.Ordinal);
        Assert.Contains("立即运行", js, StringComparison.Ordinal);
    }
}
