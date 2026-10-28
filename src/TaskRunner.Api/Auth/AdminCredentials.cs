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

    /// <summary>仅 Header / Basic（Cookie 走 Cookie Authentication 会话票据，不再比对密钥明文）。</summary>
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

    /// <summary>
    /// 账号密码校验：两侧先 SHA-256 再恒定时间比较——长度差异不再泄漏，且与
    /// MatchesKey 的逐字节比较同级安全。生产未配置密码时直接拒绝。
    /// </summary>
    public static bool VerifyLogin(string username, string password, TaskRunnerOptions options)
    {
        var configuredPassword = options.AdminPassword;
        if (string.IsNullOrEmpty(configuredPassword))
        {
            return false;
        }

        return FixedTimeHash(username, options.AdminUsername)
            && FixedTimeHash(password, configuredPassword);
    }

    /// <summary>摘要后恒定时间比较；任意一侧为空立即 false。</summary>
    private static bool FixedTimeHash(string candidate, string configured)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(configured))
        {
            return false;
        }

        var left = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>恒定时间比较；长度不同立即 false（避免 FixedTimeEquals 抛异常）。</summary>
    public static bool MatchesKey(string candidate, string adminApiKey)
    {
        var left = Encoding.UTF8.GetBytes(candidate);
        var right = Encoding.UTF8.GetBytes(adminApiKey);
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static AuthenticationTicket CreateTicket(string identity, string authenticationType)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identity),
            new Claim(ClaimTypes.Role, AdminAuthDefaults.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
        return new AuthenticationTicket(principal, authenticationType);
    }

    public static ClaimsPrincipal CreatePrincipal(string identity, string authenticationType)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identity),
            new Claim(ClaimTypes.Role, AdminAuthDefaults.Role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
