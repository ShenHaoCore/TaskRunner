using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Filters;

public sealed class ReadOnlyGuardFilter(IOptions<TaskRunnerOptions> options) : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && options.Value.ReadOnlyMode)
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
}
