using System.Security.Claims;
using System.Text;
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
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("development")));
        }

        var configured = hangfireOptions.Value.AdminApiKey;
        if (string.IsNullOrEmpty(configured))
        {
            return Task.FromResult(AuthenticateResult.Fail("未配置 Hangfire:AdminApiKey。"));
        }

        if (Request.Headers.TryGetValue(AdminAuthDefaults.HeaderName, out var header)
            && string.Equals(header.ToString(), configured, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("header")));
        }

        if (Request.Cookies.TryGetValue(AdminAuthDefaults.CookieName, out var cookie)
            && string.Equals(cookie, configured, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("cookie")));
        }

        if (TryReadBasic(configured, out var basicIdentity))
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket(basicIdentity)));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private bool TryReadBasic(string configured, out string identity)
    {
        identity = "basic";
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2)
            {
                return false;
            }

            // 用户名任意，密码必须等于 AdminApiKey；推荐 admin:<key>
            return string.Equals(parts[1], configured, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static AuthenticationTicket CreateTicket(string identity)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identity),
            new Claim(ClaimTypes.Role, AdminAuthDefaults.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AdminAuthDefaults.Scheme));
        return new AuthenticationTicket(principal, AdminAuthDefaults.Scheme);
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
