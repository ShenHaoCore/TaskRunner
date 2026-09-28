using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Tasks;

namespace TaskRunner.Bilibili;

public static class BilibiliServiceCollectionExtensions
{
    private const string HttpClientName = "TaskRunner.Bilibili";

    public static IServiceCollection AddBilibili(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<BilibiliOptions>(configuration.GetSection(BilibiliOptions.SectionName));
        services.AddSingleton<IBilibiliCookieStore, BilibiliFileCookieStore>();

        // UseCookies=false：保留 Set-Cookie 响应头，供扫码成功/补齐 buvid3 时自行组装 Cookie。
        services.AddHttpClient(HttpClientName, client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BilibiliHttpClient.BrowserUserAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

        // Scoped：一次作业执行内，Nav 拿到的 wbi 钥匙被各领域 API 共享。
        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new BilibiliHttpClient(factory.CreateClient(HttpClientName), sp.GetRequiredService<IBilibiliCookieStore>());
        });

        services.AddScoped<IBilibiliAccountApi, BilibiliAccountApi>();
        services.AddScoped<IBilibiliVideoApi, BilibiliVideoApi>();
        services.AddScoped<IBilibiliLiveApi, BilibiliLiveApi>();
        services.AddScoped<IBilibiliMangaApi, BilibiliMangaApi>();
        services.AddScoped<IBilibiliVipApi, BilibiliVipApi>();
        services.AddScoped<IBilibiliChargeApi, BilibiliChargeApi>();
        services.AddScoped<IBilibiliVideoPicker, BilibiliVideoPicker>();

        // 8 个独立任务，可单独调用或组合进管线。
        services.AddScoped<IWatchShareTask, WatchShareTask>();
        services.AddScoped<ILikeTask, LikeTask>();
        services.AddScoped<ICoinTask, CoinTask>();
        services.AddScoped<IMangaSignTask, MangaSignTask>();
        services.AddScoped<ILiveSignTask, LiveSignTask>();
        services.AddScoped<ISilverTask, SilverTask>();
        services.AddScoped<IVipPrivilegeTask, VipPrivilegeTask>();
        services.AddScoped<IChargeTask, ChargeTask>();

        services.AddScoped<IBilibiliQrLoginService, BilibiliQrLoginService>();
        services.AddScoped<IBilibiliDailyPipeline, BilibiliDailyPipeline>();
        return services;
    }
}
