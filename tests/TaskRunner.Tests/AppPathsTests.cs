using TaskRunner.Core.Common;

namespace TaskRunner.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ResolveSqliteFile_UsesSolutionRoot()
    {
        var root = Directory.CreateTempSubdirectory("taskrunner-paths-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, AppPaths.SolutionFileName), " ");
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "Api"));
            var path = AppPaths.ResolveSqliteFile("Data Source=data/hangfire.db", project.FullName);

            Assert.Equal(Path.GetFullPath(Path.Combine(root.FullName, "data", "hangfire.db")), Path.GetFullPath(path));
            Assert.True(Directory.Exists(Path.Combine(root.FullName, "data")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
