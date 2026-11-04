using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Codex.Mcp.Tests;

// upload_image, list_images and get_image (#65) over stdio: the process reads the file at a path itself, so no image
// bytes pass through a tool call. The stdio process makes no renditions (the web host's worker does), so an upload
// stays processing here; McpImageUploadHttpTests covers the renditions and viewing.
[TestFixture]
[CancelAfter(60000)]
public class McpImageToolsTests
{
    private static readonly string PlayersGuide = SeedIds.Books.PlayersGuide.ToString();

    private McpStdioServer _server = null!;
    private string _image = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test, Order(1)]
    public async Task UploadImage_WhenGivenAPngPath_ShouldStoreItForTheBook()
    {
        var path = TestPng.Write("part-1-opener.png", 600, 400);

        var result = await Client.OkAsync("upload_image", new { path, book = PlayersGuide, source = "Midjourney" });

        var image = result.GetProperty("image");
        _image = image.GetProperty("id").GetString()!;
        (image.GetProperty("filename").GetString(), image.GetProperty("width_px").GetInt32(), image.GetProperty("height_px").GetInt32())
            .ShouldBe(("part-1-opener.png", 600, 400));
        image.GetProperty("scope").GetString().ShouldBe("book");
        image.GetProperty("status").GetString().ShouldBe("processing");
        image.GetProperty("bytes").GetInt64().ShouldBe(new FileInfo(path).Length);
    }

    [Test, Order(2)]
    public async Task ListImages_WhenTheTemplatesAreNotReadable_ShouldFindTheUploadAndSayWhyThereIsNoDpi()
    {
        // The stdio test process has no CODEX_TEMPLATES_ROOT; McpImageUploadHttpTests checks the dpi on the web host.
        var result = await Client.OkAsync("list_images", new { book = PlayersGuide, filename = "part-1" });

        var image = result.GetProperty("images").EnumerateArray().ShouldHaveSingleItem();
        image.GetProperty("id").GetString().ShouldBe(_image);
        image.TryGetProperty("dpi", out _).ShouldBeFalse();
        result.GetProperty("dpi_note").GetString()!.ShouldContain("CODEX_TEMPLATES_ROOT");
    }

    [Test, Order(3)]
    public async Task GetImage_WhenViewedBeforeItsRenditionExists_ShouldSaySoAndAttachNothing()
    {
        var result = await Client.CallToolAsync("get_image", new Dictionary<string, object?> { ["image"] = _image, ["view"] = true },
            cancellationToken: McpCalls.Token);

        result.IsError.ShouldNotBe(true);
        result.Content.OfType<ImageContentBlock>().ShouldBeEmpty();
        var body = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement;
        body.GetProperty("note").GetString()!.ShouldContain("processing");
    }

    [Test, Order(4)]
    public async Task UploadImage_WhenScopeIsSeries_ShouldStoreItForTheSeries()
    {
        var result = await Client.OkAsync("upload_image", new { path = TestPng.Write("crest.png", 64, 64), book = PlayersGuide, scope = "series" });

        result.GetProperty("image").GetProperty("scope").GetString().ShouldBe("series");
    }

    [TestCase("relative.png")]
    [TestCase("/no/such/file.png")]
    public async Task UploadImage_WhenThePathIsNotAnAbsoluteFile_ShouldBeInvalid(string path)
    {
        var error = await Client.ErrorAsync("upload_image", new { path, book = PlayersGuide });

        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "path"));
    }

    [Test]
    public async Task UploadImage_WhenTheFileIsNotAnImageType_ShouldBeInvalid()
    {
        var path = Path.ChangeExtension(TestPng.Write("x.png", 8, 8), ".gif");
        File.Move(Path.ChangeExtension(path, ".png"), path);

        var error = await Client.ErrorAsync("upload_image", new { path, book = PlayersGuide });

        error.GetProperty("field").GetString().ShouldBe("path");
    }

    [Test]
    public async Task CreateImageUpload_WhenCalledOverStdio_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("create_image_upload", new { filename = "a.png", content_type = "image/png", bytes = 10 });

        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "name"));
    }
}
