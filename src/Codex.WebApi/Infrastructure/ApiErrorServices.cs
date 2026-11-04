using Codex.Contracts;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Codex.WebApi.Infrastructure;

// Wires the error shape without touching Program.cs (the developer-page hook covers Development, where the
// host's developer exception page would otherwise swallow the exception): a startup filter puts the ApiException handler first in the
// pipeline, and [ApiController] model-state failures become ApiError { code: "validation" } too. Framework
// responses under /api with an error status and an empty body (unknown route, wrong method, binding failure
// in Production) get an ApiError body as well, so every /api error a client sees is { error, code }.
public sealed class ApiErrorServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IStartupFilter, ApiErrorStartupFilter>();
        services.AddSingleton<IDeveloperPageExceptionFilter, ApiErrorDeveloperPageFilter>();
        services.PostConfigure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                var message = context.ModelState.Values
                    .SelectMany(entry => entry.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? "The request is not valid.";
                return new BadRequestObjectResult(new ApiError(message, ApiErrorCodes.Validation));
            });
    }

    private sealed class ApiErrorStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                try
                {
                    await nextMiddleware();
                    if (IsBodilessApiError(context.Request.Path, context.Response))
                    {
                        await ApiErrorWriter.WriteForStatusAsync(context.Request, context.Response, context.RequestAborted);
                    }
                }
                catch (Exception e) when (!context.Response.HasStarted && e is not OperationCanceledException)
                {
                    if (e is not ApiException)
                    {
                        context.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger<ApiErrorServices>()
                            .LogError(e, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                    }
                    await ApiErrorWriter.WriteAsync(context.Response, e, context.RequestAborted);
                }
            });
            next(app);
        };

        private static bool IsBodilessApiError(PathString path, HttpResponse response)
            => path.StartsWithSegments("/api")
               && response is { HasStarted: false, StatusCode: >= 400, ContentLength: null }
               && string.IsNullOrEmpty(response.ContentType);
    }
}
