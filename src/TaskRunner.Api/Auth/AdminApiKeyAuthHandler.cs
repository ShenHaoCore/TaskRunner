using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
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

        return services;
    }
}