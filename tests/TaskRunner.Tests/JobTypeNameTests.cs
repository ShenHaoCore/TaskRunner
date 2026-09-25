using TaskRunner.Bilibili.Jobs;
using TaskRunner.Core.Common;

namespace TaskRunner.Tests;

public sealed class JobTypeNameTests
{
    [Fact]
    public void For_UsesFullNameAndAssemblySimpleName()
    {
        var name = JobTypeName.For(typeof(BilibiliDailyJob));
        Assert.Equal("TaskRunner.Bilibili.Jobs.BilibiliDailyJob, TaskRunner.Bilibili", name);
        Assert.NotNull(JobTypeResolver.Resolve(name));
    }
}
