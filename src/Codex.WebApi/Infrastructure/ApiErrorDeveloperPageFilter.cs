using Codex.Contracts.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace Codex.WebApi.Infrastructure;

// In Development the host's developer exception page sits inside the pipeline filter in ApiErrorServices and
// would turn every exception into a 500 HTML page. This hook hands them back as the ApiError body instead, so
// development and tests see the same shape as production (the middleware already logged the exception).
public sealed class ApiErrorDeveloperPageFilter : IDeveloperPageExceptionFilter
{
    public async Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next)
    {
        if (!errorContext.HttpContext.Response.HasStarted)
        {
            await ApiErrorWriter.WriteAsync(
                errorContext.HttpContext.Response, errorContext.Exception, errorContext.HttpContext.RequestAborted);
            return;
        }
        await next(errorContext);
    }
}
