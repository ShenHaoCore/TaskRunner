using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace TaskRunner.Core.Common;

public static class SharedConfigurationExtensions
{
    /// <summary>
    /// 加载 Api/Worker 共享的 JSON 配置（源文件在 <c>src/appsettings*.json</c>，构建时复制到输出目录）。
    /// 替换宿主默认的 ContentRoot JSON 源，统一从 <see cref="AppContext.BaseDirectory"/> 读取；
    /// 优先级仍低于环境变量与命令行参数。
    /// </summary>
    public static IConfigurationBuilder AddSharedConfiguration(this IConfigurationBuilder configuration, string environmentName)
    {
        for (var i = configuration.Sources.Count - 1; i >= 0; i--)
        {
            // 只移除宿主默认的 appsettings*.json 源（重新指向 BaseDirectory 的共享副本）。
            // 不能按 JsonConfigurationSource 整类移除：UserSecretsConfigurationSource 继承自它，
            // 误删会让 Development 下 user secrets 失效、回退到共享默认连接串（CHANGE_ME）。
            if (configuration.Sources[i] is JsonConfigurationSource json
                && json.Path?.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) == true)
            {
                configuration.Sources.RemoveAt(i);
            }
        }

        configuration.Sources.Insert(0, CreateSource("appsettings.json", optional: false));
        configuration.Sources.Insert(1, CreateSource($"appsettings.{environmentName}.json", optional: true));
        return configuration;
    }

    private static JsonConfigurationSource CreateSource(string fileName, bool optional) => new()
    {
        Path = fileName,
        Optional = optional,
        ReloadOnChange = true,
        FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory)
    };
}
