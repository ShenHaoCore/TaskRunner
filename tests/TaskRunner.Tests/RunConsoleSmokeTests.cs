namespace TaskRunner.Tests;

/// <summary>票 07：运行台静态页冒烟（路径与文案；鉴权/trigger 由 REST 缝覆盖）。</summary>
public sealed class RunConsoleSmokeTests
{
    [Fact]
    public void RunHtml_ExistsInApiWwwroot()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "TaskRunner.Api", "wwwroot", "run.html"));
        Assert.True(File.Exists(path), $"找不到运行台页面：{path}");
        var html = File.ReadAllText(path);
        Assert.Contains("任务运行台", html, StringComparison.Ordinal);
        Assert.Contains("/api/tasks", html, StringComparison.Ordinal);
        Assert.Contains("立即运行", html, StringComparison.Ordinal);
    }
}
