using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Filters;

public sealed class ReadOnlyGuardFilter(IRuntimeSettingsStore settings) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            && await settings.GetReadOnlyModeAsync(context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new { message = "当前为只读模式。" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
