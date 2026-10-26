using TaskRunner.Core.Common;

namespace TaskRunner.Tests;

public sealed class SampleJob;

public sealed class JobTypeNameTests
{
    [Fact]
    public void For_UsesFullNameAndAssemblySimpleName()
    {
        var name = JobTypeName.For(typeof(SampleJob));
        Assert.Equal("TaskRunner.Tests.SampleJob, TaskRunner.Tests", name);
        Assert.NotNull(JobTypeResolver.Resolve(name));
    }
}
