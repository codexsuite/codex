using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;

namespace Codex.WebApi.Tests;

// The one way an API test reads an error: the body must be exactly { code, error } with a sentence in error.
public static class ApiErrors
{
    public static async Task<ApiError> ReadApiErrorAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["code", "error"]);
        var error = body.Deserialize<ApiError>(JsonSerializerOptions.Web)!;
        error.Error.ShouldNotBeNullOrEmpty();
        return error;
    }

    public static async Task<ApiError> ReadApiErrorAsync(this HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        return await response.ReadApiErrorAsync();
    }
}
