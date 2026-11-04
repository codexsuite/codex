using System.ComponentModel;
using Codex.Application.Features.Print;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. Art (#65): getting images into Codex without their bytes passing through the model, and reading them back. Both
// upload routes end in IAssetService.UploadAsync, so the limits (PNG, JPEG or WebP, 25 MB, 64 MP), probing and
// renditions are the Assets panel's:
//   upload_image         stdio only: Codex.Mcp reads the file at `path` itself (it runs on the machine that holds it).
//   create_image_upload  HTTP only: a single-use link (IAssetUploadLinks) the agent PUTs the file to with curl.
// McpTransportTools hides each from the other transport's tool list. The asset services need the blob store, so they
// are resolved per call: a stdio server without CODEX_BLOB_ROOT still serves every other tool.
[McpServerToolType]
public sealed class ImageTools(McpRefs refs, McpAccess access, IBookAccess bookAccess, IServiceProvider services)
{
    public sealed record ImageInfo(
        Guid Id, string Filename, string Scope, string Status, long Bytes, int? WidthPx, int? HeightPx, string? Error, AssetDpiDto? Dpi,
        string OpenUrl, string Created);

    public sealed record ImageResult(ImageInfo Image) : McpResult;

    public sealed record ImageListResult(IReadOnlyList<ImageInfo> Images, int Total, string? DpiNote) : McpResult;

    public sealed record ViewResult(ImageInfo Image, string? Note, string? DpiNote) : McpResult;

    public sealed record UploadLinkResult(string UploadUrl, string Method, string ExpiresAt, int ExpiresInSeconds, string Curl) : McpResult;

    [McpServerTool(Name = "upload_image", Title = "Upload an image from this machine", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Uploads the PNG, JPEG or WebP file at `path` (on the machine this server runs on; at most 25 MB and 64 megapixels) to " +
                 "the book, or with scope \"series\" to its series. Returns the image id to pass to place_figure or set_opener, its pixel size " +
                 "and status (processing until its renditions are made). Call list_images first to skip files already uploaded.")]
    public async Task<ImageResult> UploadImageAsync(
        McpServer server,
        [Description("Absolute path of the image file.")] string path,
        [Description("The book (default: the most recently edited book).")] string? book = null,
        [Description("book (default) or series.")] string scope = "book",
        [Description("Where the image came from (kept with it).")] string? source = null,
        [Description("The prompt that generated it.")] string? prompt = null,
        [Description("Its licence.")] string? licence = null,
        CancellationToken cancellationToken = default)
    {
        McpTransportTools.Require(server, UploadImage);
        var owner = await ScopeAsync(book, scope, cancellationToken);
        if (!Path.IsPathFullyQualified(path))
        {
            throw McpToolException.Invalid("path", "path must be absolute.");
        }
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw McpToolException.Invalid("path", $"There is no file at {path}.");
        }
        if (file.Length > AssetService.MaxUploadBytes)
        {
            throw McpToolException.Invalid("path", $"Images can be at most {AssetService.MaxUploadBytes / (1024 * 1024)} MB; this file is {file.Length} bytes.");
        }
        var type = ContentType(file.Extension) ?? throw McpToolException.Invalid("path", "Upload a .png, .jpg, .jpeg or .webp file.");

        var assets = Service<IAssetService>();
        await using var content = file.OpenRead();
        var asset = await assets.UploadAsync(owner, new AssetUpload(file.Name, type, file.Length, content, source, prompt, licence), cancellationToken);
        return new ImageResult(Info(asset)) { Item = Item(asset) };
    }

    [McpServerTool(Name = "create_image_upload", Title = "Get a link to upload an image to", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("For an image the server can't read from disk: returns a single-use upload_url for the book (or, with scope \"series\", its " +
                 "series), valid for 10 minutes. PUT the file to it, e.g. curl --fail -T <file> <upload_url>; the response is the image (its id " +
                 "goes to place_figure or set_opener). Declare the file's name, content_type (image/png, image/jpeg or image/webp) and exact size " +
                 "in bytes (at most 25 MB); a body of another size is refused.")]
    public async Task<UploadLinkResult> CreateImageUploadAsync(
        McpServer server,
        [Description("The file's name, e.g. part-1-opener.png.")] string filename,
        [Description("image/png, image/jpeg or image/webp.")] string content_type,
        [Description("The file's size in bytes.")] long bytes,
        [Description("The book (default: the most recently edited book).")] string? book = null,
        [Description("book (default) or series.")] string scope = "book",
        [Description("Where the image came from (kept with it).")] string? source = null,
        [Description("The prompt that generated it.")] string? prompt = null,
        [Description("Its licence.")] string? licence = null,
        CancellationToken cancellationToken = default)
    {
        McpTransportTools.Require(server, CreateImageUpload);
        var owner = await ScopeAsync(book, scope, cancellationToken);
        var request = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request
                      ?? throw new InvalidOperationException("create_image_upload runs only inside an HTTP request.");
        var link = await Service<IAssetUploadLinks>().CreateAsync(owner,
            new AssetUploadLinkRequest(filename, content_type, bytes, source, prompt, licence), access.Current.TokenId, cancellationToken);
        var url = $"{request.Scheme}://{request.Host}/api/asset-uploads/{link.Token}";
        return new UploadLinkResult(url, "PUT", link.ExpiresAt.ToString("O"), (int)AssetUploadLinks.Lifetime.TotalSeconds,
            $"curl --fail -T '{filename.Replace("'", "'\\''")}' '{url}'");
    }

    [McpServerTool(Name = "list_images", Title = "List a book's images", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The images the book can place: its own and its series', newest first, with status, size in bytes and pixels, and the " +
                 "effective dpi at `placement` in the book's layout. Match on filename and bytes to skip files already uploaded.")]
    public async Task<ImageListResult> ListImagesAsync(
        [Description("The book (default: the most recently edited book).")] string? book = null,
        [Description("Only images whose filename contains this.")] string? filename = null,
        [Description("The placement the dpi is judged for: column (default), page, spread or float-top.")] string placement = "column",
        CancellationToken cancellationToken = default)
    {
        var target = await refs.BookAsync(book, cancellationToken);
        var (dpi, note) = await DpiRequestAsync(target.Id, placement, cancellationToken);
        var images = (await Service<IAssetService>().ListAsync(target.Id, dpi, cancellationToken))
            .Where(a => filename == null || a.Filename.Contains(filename, StringComparison.OrdinalIgnoreCase))
            .Select(Info).ToList();
        return new ImageListResult(images, images.Count, note) { Item = target.Item };
    }

    [McpServerTool(Name = "get_image", Title = "Look at an image", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("One image's status, size and effective dpi at `placement` in `book`'s layout (default: the image's own book). With view: true " +
                 "the result also carries the image itself (its web rendition, about 1600 px), so you can look at it to write alt text or a " +
                 "caption; until the rendition is made it carries none and says so.")]
    public async Task<CallToolResult> GetImageAsync(
        [Description("The image's asset id.")] Guid image,
        [Description("Attach the image so you can see it.")] bool view = false,
        [Description("The placement the dpi is judged for: column (default), page, spread or float-top.")] string placement = "column",
        [Description("The book whose layout the dpi is judged in (needed for a series image).")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var assets = Service<IAssetService>();
        var asset = await assets.GetAsync(image, null, cancellationToken);
        var layoutBook = asset.Scope == "book" ? asset.BookId : null;
        if (book != null)
        {
            layoutBook = (await refs.BookAsync(book, cancellationToken)).Id;
        }

        var content = new List<ContentBlock>();
        string? note = null;
        string? dpiNote = null;
        if (layoutBook is { } bookId)
        {
            (var dpi, dpiNote) = await DpiRequestAsync(bookId, placement, cancellationToken);
            asset = await assets.GetAsync(image, dpi, cancellationToken);
        }

        if (view)
        {
            if (asset.Renditions.Any(r => r.Purpose == "web"))
            {
                var file = await assets.OpenAsync(image, "web", cancellationToken);
                await using var stream = file.Content;
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes, cancellationToken);
                content.Add(ImageContentBlock.FromBytes(bytes.ToArray(), file.ContentType));
            }
            else
            {
                note = $"The image is {asset.Status}: its web rendition isn't made yet, so it isn't attached. Try again when status is ready.";
            }
        }

        var result = new ViewResult(Info(asset), note, dpiNote) { Item = Item(asset) };
        content.Insert(0, new TextContentBlock { Text = McpJson.Serialize(result) });
        return new CallToolResult { Content = content };
    }

    public const string UploadImage = "upload_image";

    public const string CreateImageUpload = "create_image_upload";

    private static ImageInfo Info(AssetDto a) =>
        new(a.Id, a.Filename, a.Scope, a.Status, a.Bytes, a.WidthPx, a.HeightPx, a.Error, a.Dpi, a.Url, a.CreatedLabel);

    private static McpItem Item(AssetDto a) => new(a.Id, "image", a.Filename, a.Url);

    private static string? ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null,
    };

    // The book, or its series with scope "series", that an upload goes to; uploading needs write there (#80), so a
    // series image needs write on the series, not just on one of its books.
    private async Task<Guid> ScopeAsync(string? book, string scope, CancellationToken cancellationToken)
    {
        var target = await refs.BookAsync(book, cancellationToken);
        var id = scope switch
        {
            "book" => target.Id,
            "series" => target.SeriesId ?? throw McpToolException.Invalid("scope", $"{target.Label} is not in a series."),
            _ => throw McpToolException.Invalid("scope", "scope is book or series."),
        };
        await bookAccess.RequireAsync(id, BookRole.Write, cancellationToken);
        return id;
    }

    // The book's layout (template, trim, columns) the dpi is judged in. A server that can't read the template packages
    // (a stdio process without CODEX_TEMPLATES_ROOT) still lists the images: without dpi, saying why.
    private async Task<(AssetDpiRequest? Request, string? Note)> DpiRequestAsync(Guid bookId, string placement, CancellationToken cancellationToken)
    {
        var place = placement.Trim().ToLowerInvariant();
        if (!FigureCommands.Placements.Contains(place))
        {
            throw McpToolException.Invalid("placement", "placement is column, page, spread or float-top.");
        }
        try
        {
            var (template, trim, _) = await Service<IRenderService>().ResolveAsync(new RenderRequest(bookId, RenderScope.Book), cancellationToken);
            return (new AssetDpiRequest(trim, template.Page.Columns, place), null);
        }
        catch (ApiException e) when (e.Code is ApiErrorCodes.NotFound or ApiErrorCodes.NotConfigured)
        {
            return (null, $"No dpi: {e.Message} Set {RenderSettings.TemplatesRootKey} to the repo's templates folder, or see Layout's preflight.");
        }
    }

    private T Service<T>() where T : notnull
    {
        try
        {
            return services.GetRequiredService<T>();
        }
        catch (InvalidOperationException e) when (e.Message.Contains("CODEX_BLOB_ROOT", StringComparison.Ordinal))
        {
            throw new McpToolException(McpErrorCodes.NotConfigured,
                "Images live in the blob store, and this server has no CODEX_BLOB_ROOT. Set it to the folder the web app uses.");
        }
    }
}
