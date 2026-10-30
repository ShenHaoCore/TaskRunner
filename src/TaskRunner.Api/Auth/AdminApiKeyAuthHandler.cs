using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Auth;

public static class AdminAuthDefaults
{
    public const string Scheme = "TaskRunnerAdmin";
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string Policy = "TaskRunnerAdmin";
    public const string Role = "Admin";
    public const string HeaderName = "X-TaskRunner-Admin";
    public const string CookieName = "TaskRunner.Admin";
    public const string LoginRateLimitPolicy = "admin-login";
}

public sealed class AdminApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    IOptions<TaskRunnerOptions> hangfireOptions,
    IHostEnvironment environment) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// 未认证的挑战：浏览器请求（Accept 含 text/html）302 重定向到 /login 并携带 returnUrl，
    /// 登录后回到原页面；机器请求（curl 等）保持 401。
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (BrowserRedirect.IsBrowserRequest(Request))
        {
            BrowserRedirect.RedirectToLogin(Response, Request);
            return Task.CompletedTask;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Success(
                AdminCredentials.CreateTicket("development", AdminAuthDefaults.Scheme)));
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

        var identity = Request.Headers.ContainsKey(AdminAuthDefaults.HeaderName) ? "header" : "basic";
        return Task.FromResult(AuthenticateResult.Success(
            AdminCredentials.CreateTicket(identity, AdminAuthDefaults.Scheme)));
    }
}

public static class AdminAuthServiceCollectionExtensions
{
    public static IServiceCollection AddTaskRunnerAdminAuth(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AdminAuthDefaults.CookieScheme;
                options.DefaultSignInScheme = AdminAuthDefaults.CookieScheme;
                options.DefaultChallengeScheme = AdminAuthDefaults.Scheme;
            })
            .AddCookie(AdminAuthDefaults.CookieScheme, options =>
            {
                options.Cookie.Name = AdminAuthDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, AdminApiKeyAuthHandler>(AdminAuthDefaults.Scheme, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(AdminAuthDefaults.CookieScheme)
            .Configure<IOptions<TaskRunnerOptions>>((options, hangfire) =>
            {
                options.Cookie.SecurePolicy = hangfire.Value.CookieSecure
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(Math.Max(1, hangfire.Value.SessionCookieDays));
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminAuthDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(AdminAuthDefaults.CookieScheme, AdminAuthDefaults.Scheme);
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

                var permitLimit = Math.Max(1, context.RequestServices
                    .GetRequiredService<IOptions<TaskRunnerOptions>>().Value.LoginRateLimitPerMinute);
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });
        });

        services.AddSingleton<IStartupFilter, AdminKeyGuardStartupFilter>();
        return services;
    }
}
