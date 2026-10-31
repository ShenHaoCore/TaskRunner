using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Filters;

public sealed class ReadOnlyGuardFilter(IOptions<TaskRunnerOptions> options) : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 只读挡的是「任务写操作」，不是认证流——登录/登出被 403 拦死会让只读模式下
        // 连登录页都提交不了（已登录用户也无法登出）。
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            && !IsAuthFlow(context.HttpContext.Request.Path)
            && options.Value.ReadOnlyMode)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "只读模式",
                Detail = "当前为只读模式（Hangfire:ReadOnlyMode），写操作被拦截。"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return Task.CompletedTask;
        }

        return next();
    }

    private static bool IsAuthFlow(PathString path) =>
        path.StartsWithSegments("/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/logout", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);
}
