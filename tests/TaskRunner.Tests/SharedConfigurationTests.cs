using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using TaskRunner.Core.Common;

namespace TaskRunner.Tests;

/// <summary>
/// 共享配置装配契约：替换宿主默认 appsettings*.json 的同时，必须保留
/// user secrets 等其他 Json 派生源（UserSecretsConfigurationSource 继承自 JsonConfigurationSource，
/// 整类移除会让 Development 下 secrets 失效、回退到共享默认连接串）。
/// </summary>
public sealed class SharedConfigurationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"tr-secrets-{Guid.NewGuid():N}");

    [Fact]
    public void AddSharedConfiguration_KeepsNonAppsettingsJsonSources()
    {
        Directory.CreateDirectory(_tempDir);
        var secretsFile = Path.Combine(_tempDir, "secrets.json");
        File.WriteAllText(secretsFile,
            """{ "ConnectionStrings:Default": "Host=127.0.0.1;Port=5432;Password=from-secrets" }""");

        var builder = new ConfigurationBuilder();
        builder.AddJsonFile("appsettings.json", optional: false); // 宿主默认源（tests bin 内存在）
        builder.Sources.Add(new JsonConfigurationSource
        {
            Path = "secrets.json", // user secrets 源的 Path 形态
            Optional = false,
            FileProvider = new PhysicalFileProvider(_tempDir)
        });

        builder.AddSharedConfiguration("Development");
        var configuration = builder.Build();

        // secrets 仍在链上且优先于共享 appsettings → 覆盖 localhost/CHANGE_ME 默认
        Assert.StartsWith("Host=127.0.0.1", configuration["ConnectionStrings:Default"], StringComparison.Ordinal);
        // 共享 appsettings 仍生效（兜底键）
        Assert.Equal("PostgreSql", configuration["Hangfire:Storage"]);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }
}
