using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Auth;

/// <summary>Dashboard 与 ASP.NET Auth 共用的管理员校验。</summary>
public static class AdminCredentials
{
    public static bool IsAuthorized(HttpContext http, TaskRunnerOptions options, bool isDevelopment)
    {
        if (isDevelopment)
        {
            return true;
        }

        if (http.User.Identity?.IsAuthenticated == true && http.User.IsInRole(AdminAuthDefaults.Role))
        {
            return true;
        }

        return TryMatchRequest(http.Request, options.AdminApiKey);
    }

    public static bool TryMatchRequest(HttpRequest request, string? adminApiKey)
    {
        if (string.IsNullOrEmpty(adminApiKey))
        {
            return false;
        }

        if (request.Headers.TryGetValue(AdminAuthDefaults.HeaderName, out var header)
            && MatchesKey(header.ToString(), adminApiKey))
        {
            return true;
        }

        if (request.Cookies.TryGetValue(AdminAuthDefaults.CookieName, out var cookie)
            && MatchesKey(cookie, adminApiKey))
        {
            return true;
        }

        return TryMatchBasic(request, adminApiKey);
    }

    public static bool TryMatchBasic(HttpRequest request, string adminApiKey)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..].Trim()));
            var parts = raw.Split(':', 2);
            return parts.Length == 2 && MatchesKey(parts[1], adminApiKey);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>恒定时间比较，防时序侧信道；长度不同立即 false（泄露长度可接受）。</summary>
    public static bool MatchesKey(string candidate, string adminApiKey)
    {
        var left = Encoding.UTF8.GetBytes(candidate);
        var right = Encoding.UTF8.GetBytes(adminApiKey);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static AuthenticationTicket CreateTicket(string identity)
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