namespace TaskRunner.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Api_DoesNotReferenceWorker()
    {
        var apiPath = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Api", "TaskRunner.Api.csproj");
        Assert.True(File.Exists(apiPath), $"找不到 Api 工程：{apiPath}");
        var text = File.ReadAllText(apiPath);
        Assert.DoesNotContain("TaskRunner.Worker", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TaskRunner.Hangfire", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TaskRunner.Bilibili", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HangfireProject_ReferencesConsolePackage()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Hangfire", "TaskRunner.Hangfire.csproj");
        Assert.True(File.Exists(path), $"找不到 Hangfire 工程：{path}");
        var text = File.ReadAllText(path);
        Assert.Contains("IdentityStream.Hangfire.Console", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dashboard.Forms", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreProject_HasZeroHangfireDependency()
    {
        // README 明文承诺「Core 无 Hangfire」——契约层与引擎解耦是本仓库最重要的分层约束
        var coreDir = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Core");
        var csproj = File.ReadAllText(Path.Combine(coreDir, "TaskRunner.Core.csproj"));
        Assert.DoesNotContain("Hangfire", csproj, StringComparison.OrdinalIgnoreCase);

        foreach (var source in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(source);
            Assert.True(!text.Contains("using Hangfire", StringComparison.Ordinal),
                $"Core 源文件不得引用 Hangfire：{Path.GetRelativePath(TestPaths.RepoRoot, source)}");
        }
    }

    [Fact]
    public void Api_HangfireTouchpoints_StayInWhitelist()
    {
        // Api 承载 Dashboard 是设计选择，但 Hangfire 类型触点必须收敛在白名单文件内——
        // 将来升级引擎（1.8→2.0）或更换引擎时，改动面以本测试为界。
        string[] whitelist =
        [
            "Services/HangfireMonitoringReader.cs",
            "Hosting/HealthChecks.cs",
            "Hosting/WebApplicationExtensions.cs",
            "Dashboard/AdminDashboardAuthorizationFilter.cs"
        ];

        var apiDir = Path.Combine(TestPaths.RepoRoot, "src", "TaskRunner.Api");
        foreach (var source in Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(apiDir, source).Replace('\\', '/');
            var text = File.ReadAllText(source);
            if (!text.Contains("using Hangfire", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(whitelist.Contains(relative, StringComparer.Ordinal),
                $"Api 中引用 Hangfire 的文件超出白名单 [{string.Join(", ", whitelist)}]：{relative}");
        }
    }

    [Fact]
    public void PackageVersions_AreCentrallyManaged()
    {
        // 中央包管理纪律：版本只活在 Directory.Packages.props，csproj 不得再带 Version 属性
        var versionProps = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "Directory.Packages.props"));
        Assert.Contains("ManagePackageVersionsCentrally", versionProps, StringComparison.Ordinal);

        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, "tests"), "*.csproj", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(csproj);
            Assert.True(!text.Contains("Version=\"", StringComparison.Ordinal),
                $"PackageReference 版本必须集中在 Directory.Packages.props：{Path.GetRelativePath(TestPaths.RepoRoot, csproj)}");
        }
    }
}
