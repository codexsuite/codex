using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Shared by the asset tests: multipart upload and waiting for the in-process rendition worker by polling the API.
public abstract class AssetApiFixture : ApiFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    protected Task<HttpResponseMessage> UploadAsync(Guid bookId, string? fileName, byte[]? content, string contentType, string? source = null)
    {
        var form = new MultipartFormDataContent();
        if (content is not null)
        {
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "file", fileName ?? "upload.bin");
        }
        if (source is not null)
        {
            form.Add(new StringContent(source), "source");
        }
        return Client.PostAsync($"/api/books/{bookId}/assets", form);
    }

    protected async Task<AssetDto> UploadOkAsync(Guid bookId, string fileName, byte[] content, string contentType)
    {
        using var response = await UploadAsync(bookId, fileName, content, contentType);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    // Polls the asset until the worker has finished with it (ready or failed). The worker is woken by the upload, so
    // this normally returns on the first or second request.
    protected async Task<AssetDto> SettledAsync(Guid assetId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var asset = (await Client.GetFromJsonAsync<AssetDto>($"/api/assets/{assetId}/info"))!;
            if (asset.Status != "processing")
            {
                return asset;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the rendition worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }
}
