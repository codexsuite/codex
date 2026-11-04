using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Application.Features.Print;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace Codex.Mcp.Tests;

// create_image_upload over /mcp (#65), from another machine with a personal access token (#32): the agent asks for a
// link, PUTs the file to it, the web host's worker makes the renditions, get_image shows the picture, and the image
// placed with place_figure appears in the Layout preview with its dpi. A link is single-use, expires, stops with its
// token, and refuses a declared size it was not made for, or over 25 MB, before reading the body.
[TestFixture]
[CancelAfter(120000)]
public class McpImageUploadHttpTests
{
    private const string Remote = "203.0.113.7";
    private static readonly string PlayersGuide = SeedIds.Books.PlayersGuide.ToString();

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;
    private TestTokens.Created _token = null!;
    private McpClient _client = null!;
    private HttpClient _http = null!;
    private byte[] _png = [];
    private string _uploadUrl = "";
    private Guid _image;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        _host = CodexHost.Start(_database.ConnectionString, services => services.AddTransient<IStartupFilter, RemoteAddressStartupFilter>());
        _token = await TestTokens.CreateAsync(_database.ConnectionString, SeedIds.Users.Jd, "Claude Code", [ApiTokenScopes.Read, ApiTokenScopes.Write]);
        _client = await ClientAsync(_token.Secret);
        _http = _host.CreateClient();
        _png = TestPng.Make(900, 600);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        _http.Dispose();
        await _client.DisposeAsync();
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test, Order(1)]
    public async Task ListTools_WhenOverHttp_ShouldOfferCreateImageUploadButNotUploadImage()
    {
        var names = (await _client.ListToolsAsync(cancellationToken: McpCalls.Token)).Select(t => t.Name).ToList();

        names.ShouldContain("create_image_upload");
        names.ShouldNotContain("upload_image");
        var error = await _client.ErrorAsync("upload_image", new { path = "/tmp/x.png" });
        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "name"));
    }

    [Test, Order(2)]
    public async Task CreateImageUpload_WhenOver25Mb_ShouldBeInvalid()
    {
        var error = await _client.ErrorAsync("create_image_upload", new
        {
            book = PlayersGuide, filename = "huge.png", content_type = "image/png", bytes = AssetService.MaxUploadBytes + 1,
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test, Order(3)]
    public async Task Put_WhenTheDeclaredLengthIsOver25Mb_ShouldRefuseBeforeReadingTheBody()
    {
        var links = _host.Services.GetRequiredService<IAssetUploadLinks>();

        var thrown = await Should.ThrowAsync<ApiException>(() => links.UploadAsync("any", AssetService.MaxUploadBytes + 1, new UnreadableStream(), McpCalls.Token));

        thrown.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(4)]
    public async Task Put_WhenTheBodyIsNotTheSizeTheLinkWasMadeFor_ShouldRefuseAndKeepTheLink()
    {
        _uploadUrl = await LinkAsync(_png.Length);

        using var response = await _http.PutAsync(_uploadUrl, new ByteArrayContent(_png[..^1]), McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>(McpCalls.Token))!.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task Put_WhenTheFileMatches_ShouldStoreTheImageForTheLinksBook()
    {
        using var response = await _http.PutAsync(_uploadUrl, new ByteArrayContent(_png), McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var asset = (await response.Content.ReadFromJsonAsync<AssetDto>(McpCalls.Token))!;
        (asset.BookId, asset.Filename, asset.WidthPx, asset.HeightPx).ShouldBe((SeedIds.Books.PlayersGuide, "part-1-opener.png", 900, 600));
        _image = asset.Id;
    }

    [Test, Order(6)]
    public async Task Put_WhenTheLinkWasUsed_ShouldBeNotFound()
    {
        using var response = await _http.PutAsync(_uploadUrl, new ByteArrayContent(_png), McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ApiError>(McpCalls.Token))!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(7)]
    public async Task Put_WhenTheLinkHasExpired_ShouldBeNotFound()
    {
        var url = await LinkAsync(_png.Length);
        await using (var connection = new NpgsqlConnection(_database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE asset_upload SET expires_at = now() - interval '1 minute' WHERE used_at IS NULL");
        }

        using var response = await _http.PutAsync(url, new ByteArrayContent(_png), McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ApiError>(McpCalls.Token))!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(8)]
    public async Task Put_WhenItsTokenWasRevoked_ShouldBeNotFound()
    {
        var token = await TestTokens.CreateAsync(_database.ConnectionString, SeedIds.Users.Jd, "Short-lived", [ApiTokenScopes.Read, ApiTokenScopes.Write]);
        string url;
        await using (var client = await ClientAsync(token.Secret))
        {
            url = await LinkAsync(_png.Length, client);
        }
        await TestTokens.RevokeAsync(_database.ConnectionString, token.Id);

        using var response = await _http.PutAsync(url, new ByteArrayContent(_png), McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ApiError>(McpCalls.Token))!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(9)]
    public async Task GetImage_WhenViewedOnceReady_ShouldAttachTheWebRendition()
    {
        await ReadyAsync();

        var result = await _client.CallToolAsync("get_image", new Dictionary<string, object?> { ["image"] = _image.ToString(), ["view"] = true },
            cancellationToken: McpCalls.Token);

        result.IsError.ShouldNotBe(true);
        var picture = result.Content.OfType<ImageContentBlock>().ShouldHaveSingleItem();
        picture.MimeType.ShouldStartWith("image/");
        picture.Data.Length.ShouldBeGreaterThan(0);
        var body = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement;
        body.GetProperty("image").GetProperty("status").GetString().ShouldBe("ready");
        body.GetProperty("image").GetProperty("dpi").GetProperty("status").GetString().ShouldBe("low");
    }

    [Test, Order(10)]
    public async Task PlaceFigure_WhenTheUploadIsPlaced_ShouldShowInTheLayoutPreviewAsLowDpi()
    {
        var scene = SeedIds.Scenes.PlayersGuideLatexScene;
        var expected = await ProseVersionAsync(scene);

        await _client.OkAsync("place_figure", new { scene = scene.ToString(), at = "end", asset = _image.ToString(), expected_version = expected, caption = "Part one" });

        using var response = await _http.PostAsJsonAsync($"/api/books/{SeedIds.Books.PlayersGuide}/layout/preview",
            new LayoutPreviewRequest("chapter", SeedIds.Chapters.PlayersGuideCh1, "fantasy-rpg", null, null), McpCalls.Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<LayoutPreviewDto>(McpCalls.Token))!;
        var figure = preview.Figures.Single(f => f.AssetId == _image);
        figure.Status.ShouldBe("low");
    }

    private async Task<string> LinkAsync(long bytes, McpClient? client = null)
    {
        var result = await (client ?? _client).OkAsync("create_image_upload", new
        {
            book = PlayersGuide, filename = "part-1-opener.png", content_type = "image/png", bytes,
        });
        result.GetProperty("method").GetString().ShouldBe("PUT");
        var url = result.GetProperty("upload_url").GetString()!;
        url.ShouldStartWith($"{_host.BaseUrl}/api/asset-uploads/");
        return url;
    }

    private async Task ReadyAsync()
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var body = await _client.OkAsync("get_image", new { image = _image.ToString() });
            var status = body.GetProperty("image").GetProperty("status").GetString();
            if (status != "processing")
            {
                status.ShouldBe("ready");
                return;
            }
            // The upload woke the worker, so this is a few round trips; each poll is a real request, not a sleep.
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), "the rendition worker did not finish");
        }
    }

    private async Task<int> ProseVersionAsync(Guid scene)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT version FROM prose WHERE scene_id = @scene", new { scene });
    }

    private Task<McpClient> ClientAsync(string secret) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {secret}",
            [RemoteAddressStartupFilter.Header] = Remote,
        },
    }), cancellationToken: McpCalls.Token);

    // A body that fails the test if anything reads it.
    private sealed class UnreadableStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body was read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body was read.");
    }
}
