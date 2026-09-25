using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskRunner.Bilibili;

public static class BilibiliServiceCollectionExtensions
{
    public static IServiceCollection AddBilibili(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<BilibiliOptions>(configuration.GetSection(BilibiliOptions.SectionName));
        services.AddSingleton<IBilibiliCookieStore, FileBilibiliCookieStore>();

        // UseCookies=false：保留 Set-Cookie 响应头，供扫码成功/补齐 buvid3 时自行组装 Cookie。
        services.AddHttpClient<BiliApiClient>(ConfigureDefaults).ConfigurePrimaryHttpMessageHandler(CreateHandler);
        services.AddHttpClient<IBiliQrLoginService, BiliQrLoginService>(ConfigureDefaults).ConfigurePrimaryHttpMessageHandler(CreateHandler);

        services.AddScoped<IBilibiliDailyPipeline, BilibiliDailyPipeline>();
        return services;
    }

    private static void ConfigureDefaults(HttpClient client)
        => client.DefaultRequestHeaders.UserAgent.ParseAdd(BiliApiClient.BrowserUserAgent);

    private static HttpClientHandler CreateHandler() => new() { UseCookies = false };
}
