using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Implementations;

namespace TaskRunner.Tests;

public sealed class JobTypeNameTests
{
    [Fact]
    public void For_UsesFullNameAndAssemblySimpleName()
    {
        var name = JobTypeName.For(typeof(SyncDataJob));
        Assert.Equal("TaskRunner.Core.Jobs.Implementations.SyncDataJob, TaskRunner.Core", name);
        Assert.NotNull(JobTypeResolver.Resolve(name));
    }
}

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
    }
}
