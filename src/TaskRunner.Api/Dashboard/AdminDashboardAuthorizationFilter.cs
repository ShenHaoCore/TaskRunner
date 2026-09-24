using Hangfire.Dashboard;
using Microsoft.Extensions.Options;
using System.Text;
using TaskRunner.Api.Auth;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Dashboard;

public sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var environment = http.RequestServices.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment()) { return true; }
        if (http.User.Identity?.IsAuthenticated == true && http.User.IsInRole(AdminAuthDefaults.Role)) { return true; }
        var options = http.RequestServices.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;

        if (string.IsNullOrEmpty(options.AdminApiKey))
        {
            Challenge(http);
            return false;
        }

        if (http.Request.Headers.TryGetValue(AdminAuthDefaults.HeaderName, out var header) && string.Equals(header.ToString(), options.AdminApiKey, StringComparison.Ordinal)) { return true; }
        if (http.Request.Cookies.TryGetValue(AdminAuthDefaults.CookieName, out var cookie) && string.Equals(cookie, options.AdminApiKey, StringComparison.Ordinal)) { return true; }
        var authorization = http.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..].Trim()));
                var parts = raw.Split(':', 2);
                if (parts.Length == 2 && string.Equals(parts[1], options.AdminApiKey, StringComparison.Ordinal)) { return true; }
            }
            catch (FormatException)
            {
                // fall through
            }
        }

        Challenge(http);
        return false;
    }

    private static void Challenge(HttpContext http)
    {
        http.Response.Headers.WWWAuthenticate = "Basic realm=\"TaskRunner\"";
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
