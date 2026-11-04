using Codex.Contracts.Errors;

namespace Codex.WebApi.Features.Print;

// The upload's 'file' field, read once the template's ownership has been checked.
public static class TemplatePackageReader
{
    public static async Task<Stream> ReadAsync(HttpRequest http, CancellationToken ct)
    {
        if (!http.HasFormContentType)
        {
            throw ApiException.Validation("Send the template package as multipart form data in a 'file' field.");
        }

        var form = await http.ReadFormAsync(ct);
        var file = form.Files["file"] ?? throw ApiException.Validation("Choose a template package (.zip) to upload.");
        return file.OpenReadStream();
    }
}
