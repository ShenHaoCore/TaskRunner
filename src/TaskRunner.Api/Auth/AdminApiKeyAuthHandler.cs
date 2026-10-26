using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Auth;

public static class AdminAuthDefaults
{
    public const string Scheme = "TaskRunnerAdmin";
    public const string Policy = "TaskRunnerAdmin";
    public const string Role = "Admin";
    public const string HeaderName = "X-TaskRunner-Admin";
    public const string CookieName = "TaskRunner.Admin";
    public const string LoginRateLimitPolicy = "admin-login";
}

public sealed class AdminApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<TaskRunnerOptions> hangfireOptions,
    IHostEnvironment environment) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Success(AdminCredentials.CreateTicket("development")));
        }

        var configured = hangfireOptions.Value.AdminApiKey;
        if (string.IsNullOrEmpty(configured))
        {
            return Task.FromResult(AuthenticateResult.Fail("未配置 Hangfire:AdminApiKey。"));
        }

        if (!AdminCredentials.TryMatchRequest(Request, configured))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = Request.Headers.ContainsKey(AdminAuthDefaults.HeaderName) ? "header"
            : Request.Cookies.ContainsKey(AdminAuthDefaults.CookieName) ? "cookie"
            : "basic";
        return Task.FromResult(AuthenticateResult.Success(AdminCredentials.CreateTicket(identity)));
    }
}

public static class AdminAuthServiceCollectionExtensions
{
    public static IServiceCollection AddTaskRunnerAdminAuth(this IServiceCollection services)
    {
        services.AddAuthentication(AdminAuthDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AdminApiKeyAuthHandler>(AdminAuthDefaults.Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminAuthDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(AdminAuthDefaults.Scheme);
                policy.RequireRole(AdminAuthDefaults.Role);
            });
        });

        // 登录接口按 IP 限流，防匿名爆破密钥；Development 放开（契约测试与本机调试不受限）。
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AdminAuthDefaults.LoginRateLimitPolicy, context =>
            {
                var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
                if (environment.IsDevelopment())
                {
                    return RateLimitPartition.GetNoLimiter("admin-login-dev");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });
        });

        services.AddSingleton<IStartupFilter, AdminKeyGuardStartupFilter>();
        return services;
    }
}