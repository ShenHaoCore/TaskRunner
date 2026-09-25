using Hangfire.Dashboard;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Dashboard;

public sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var environment = http.RequestServices.GetRequiredService<IHostEnvironment>();
        var options = http.RequestServices.GetRequiredService<IOptions<TaskRunnerOptions>>().Value;
        if (AdminCredentials.IsAuthorized(http, options, environment.IsDevelopment()))
        {
            return true;
        }

        http.Response.Headers.WWWAuthenticate = "Basic realm=\"TaskRunner\"";
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }
}