using Codex.Application.Events;
using Codex.Contracts.Errors;
using Microsoft.Net.Http.Headers;

namespace Codex.WebApi.Features.Auth;

// CSRF: the cookie is SameSite=Lax, and a cookie-carrying write to /api must also be something a cross-site HTML form
// cannot send: a JSON body, or the X-Codex-Client header the SPA's fetch wrapper puts on every request (a form can set
// neither). Uploads and bodiless POSTs rely on the header. Requests without the cookie carry no ambient credentials,
// so there is nothing to forge.
public static class CrossSiteWriteGuard
{
    public static void Check(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
            || !request.Path.StartsWithSegments("/api")
            || !request.Cookies.ContainsKey(AuthServices.CookieName)
            || request.Headers.ContainsKey(InMemoryBookEvents.ClientHeader)
            || IsJson(request.ContentType))
        {
            return;
        }
        throw ApiException.Forbidden("This request was refused: a write must send JSON or the X-Codex-Client header.");
    }

    private static bool IsJson(string? contentType)
        => MediaTypeHeaderValue.TryParse(contentType, out var media)
           && string.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);
}
