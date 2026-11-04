using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// Typed calls on the permission tracker; each answers with the refreshed source panel.
public abstract class PermissionApiFixture : SourceApiFixture
{
    protected async Task<SourceDetailDto> DraftAsync(Guid sourceId)
    {
        using var response = await Client.PostAsync($"/api/references/{sourceId}/permission", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
    }

    protected async Task<SourceDetailDto> TransitionAsync(Guid requestId, string action, object? body = null)
    {
        using var response = await Client.PostAsJsonAsync($"/api/permission-requests/{requestId}/{action}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
    }

    protected Task<HttpResponseMessage> PostAsync(Guid requestId, string action, object? body = null) =>
        Client.PostAsJsonAsync($"/api/permission-requests/{requestId}/{action}", body);

    protected Task<HttpResponseMessage> GrantAsync(Guid requestId, string? fileName, byte[]? bytes, string contentType, string? creditLine = null)
    {
        var form = new MultipartFormDataContent();
        if (fileName is not null && bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(file, "letter", fileName);
        }

        if (creditLine is not null)
        {
            form.Add(new StringContent(creditLine), "creditLine");
        }

        return Client.PostAsync($"/api/permission-requests/{requestId}/granted", form);
    }
}
