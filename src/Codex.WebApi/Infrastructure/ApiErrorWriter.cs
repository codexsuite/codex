using System.Text.Json;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.WebUtilities;

namespace Codex.WebApi.Infrastructure;

// Writes the ApiError body. An ApiException keeps its code and message; a minimal-API binding failure
// (malformed JSON, a missing or unparsable parameter) is `validation` with the framework's status; anything
// else becomes `internal_error` with a fixed message, so stack traces and SQL never reach the client.
// Used by the pipeline filter in ApiErrorServices, so controllers and minimal APIs share one error shape.
public static class ApiErrorWriter
{
    public static Task WriteAsync(HttpResponse response, Exception exception, CancellationToken cancellationToken)
        => exception switch
        {
            ApiException api => WriteAsync(response, api.Status, api.ToApiError(), cancellationToken),
            BadHttpRequestException bad => WriteAsync(
                response, bad.StatusCode, new ApiError(bad.Message, ApiErrorCodes.Validation), cancellationToken),
            _ => WriteAsync(response, StatusCodes.Status500InternalServerError, ApiException.Internal().ToApiError(), cancellationToken),
        };

    // A framework response under /api that carries an error status and no body: no endpoint matched (404),
    // not signed in (401), not allowed (403), over a rate limiter (429), wrong method (405), unsupported media type (415), and so on. The status
    // stays; the body becomes an ApiError.
    public static Task WriteForStatusAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        var error = status switch
        {
            StatusCodes.Status404NotFound => new ApiError(
                $"No API endpoint matches {request.Method} {request.Path}.", ApiErrorCodes.NotFound),
            StatusCodes.Status401Unauthorized => new ApiError("Sign in to continue.", ApiErrorCodes.Unauthorized),
            StatusCodes.Status403Forbidden => new ApiError("You do not have access to this.", ApiErrorCodes.Forbidden),
            StatusCodes.Status429TooManyRequests => new ApiError(
                "Too many requests. Wait a moment and try again.", ApiErrorCodes.RateLimited),
            StatusCodes.Status405MethodNotAllowed => new ApiError(
                $"{request.Method} is not allowed on {request.Path}.", ApiErrorCodes.Validation),
            >= StatusCodes.Status500InternalServerError => ApiException.Internal().ToApiError(),
            _ => new ApiError($"{ReasonPhrases.GetReasonPhrase(status)}.", ApiErrorCodes.Validation),
        };
        return WriteAsync(response, status, error, cancellationToken);
    }

    private static async Task WriteAsync(HttpResponse response, int status, ApiError error, CancellationToken cancellationToken)
    {
        response.Clear();
        response.StatusCode = status;
        await response.WriteAsJsonAsync(error, JsonSerializerOptions.Web, cancellationToken);
    }
}
