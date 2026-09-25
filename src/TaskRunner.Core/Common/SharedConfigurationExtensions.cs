using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace TaskRunner.Core.Common;

public static class SharedConfigurationExtensions
{
    /// <summary>
    /// 加载两个宿主（Api/Worker）共享的 JSON 配置。文件以链接方式复制到输出目录，
    /// 因此统一从 <see cref="AppContext.BaseDirectory"/> 读取；优先级低于各项目自带
    /// appsettings.json、环境变量与命令行参数。
    /// </summary>
    public static IConfigurationBuilder AddSharedConfiguration(this IConfigurationBuilder configuration, string environmentName)
    {
        // 共享基础配置：插到第一个 JSON 源之前（最低 JSON 优先级）。
        var firstJson = IndexOfJsonSource(configuration.Sources, first: true);
        configuration.Sources.Insert(firstJson, CreateSource("appsettings.shared.json", optional: false));

        // 共享环境配置：插到最后一个 JSON 源之后（可覆盖基础 appsettings.json）。
        var lastJson = IndexOfJsonSource(configuration.Sources, first: false);
        configuration.Sources.Insert(lastJson + 1, CreateSource($"appsettings.{environmentName}.json", optional: true));
        return configuration;
    }

    private static int IndexOfJsonSource(IList<IConfigurationSource> sources, bool first)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            var forwardIndex = first ? i : sources.Count - 1 - i;
            if (sources[forwardIndex] is JsonConfigurationSource)
            {
                return forwardIndex;
            }
        }

        // 没有默认 JSON 源时：基础配置放最前，环境配置放最后。
        return first ? 0 : sources.Count - 1;
    }

    private static JsonConfigurationSource CreateSource(string fileName, bool optional) => new()
    {
        Path = fileName,
        Optional = optional,
        ReloadOnChange = true,
        FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory)
    };
}
