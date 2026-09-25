namespace TaskRunner.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Api_DoesNotReferenceWorker()
    {
        var apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TaskRunner.Api", "TaskRunner.Api.csproj"));
        Assert.True(File.Exists(apiPath), $"找不到 Api 工程：{apiPath}");
        var text = File.ReadAllText(apiPath);
        Assert.DoesNotContain("TaskRunner.Worker", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TaskRunner.Hangfire", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TaskRunner.Bilibili", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HangfireProject_ReferencesConsolePackage()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TaskRunner.Hangfire", "TaskRunner.Hangfire.csproj"));
        Assert.True(File.Exists(path), $"找不到 Hangfire 工程：{path}");
        var text = File.ReadAllText(path);
        Assert.Contains("IdentityStream.Hangfire.Console", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dashboard.Forms", text, StringComparison.OrdinalIgnoreCase);
    }
}
