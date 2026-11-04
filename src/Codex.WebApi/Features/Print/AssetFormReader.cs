using Codex.Application.Features.Print;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.WebApi.Features.Print;

// Reads the upload's multipart body (the 'file' field and its provenance fields) and hands it to the asset service.
public static class AssetFormReader
{
    public static async Task<AssetDto> UploadAsync(IAssetService assets, Guid bookId, HttpRequest http, CancellationToken ct)
    {
        if (!http.HasFormContentType)
        {
            throw ApiException.Validation("Send the image as multipart form data in a 'file' field.");
        }

        var form = await http.ReadFormAsync(ct);
        var file = form.Files["file"] ?? throw ApiException.Validation("Choose an image file to upload.");
        await using var content = file.OpenReadStream();
        return await assets.UploadAsync(
            bookId, new AssetUpload(file.FileName, file.ContentType, file.Length, content, form["source"], form["prompt"], form["licence"]), ct);
    }
}
