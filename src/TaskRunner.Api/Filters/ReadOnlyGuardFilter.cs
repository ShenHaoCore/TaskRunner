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
            context.Result = new ObjectResult(new { message = "当前为只读模式（Hangfire:ReadOnlyMode）。" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return Task.CompletedTask;
        }

        return next();
    }
}