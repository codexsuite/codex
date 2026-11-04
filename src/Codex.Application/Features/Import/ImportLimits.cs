using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// Caps on what one import may upload, and the check every uploaded path passes before a parser sees it.
public static class ImportLimits
{
    public const int MaxFiles = 2000;

    // Knowledge base entries one import creates; names past it are listed as skipped.
    public const int MaxEntries = 2000;

    public const long MaxTotalBytes = 25L * 1024 * 1024;

    // A larger file is skipped ("too large"), not refused: one stray video should not sink the folder.
    public const int MaxFileBytes = 2 * 1024 * 1024;

    public const string TooLarge = "Too large (over 2 MB)";

    // A zip is refused when its Markdown would expand past MaxTotalBytes, or to more than this many times its own size
    // (a text file rarely compresses past ten to one; a zip bomb does so a thousand to one).
    public const int MaxCompressionRatio = 100;

    // The ratio is only checked once the Markdown is this large, so a tiny, very repetitive file is not refused.
    public const long RatioCheckFloorBytes = 1024 * 1024;

    public const string NotMarkdown = "Not a Markdown file";

    // The sources whose upload is one archive (a Novelcrafter .zip, a .docx, a Codex backup .zip): the file may be up to MaxTotalBytes, and the
    // per-file cap applies to what the parser reads out of it instead.
    private static readonly string[] ArchiveSources = ["novelcrafter", "docx", "codex_zip"];

    // The request limit the endpoint sets: the files plus multipart overhead.
    public const long MaxRequestBytes = MaxTotalBytes + (4L * 1024 * 1024);

    // Sources whose upload also carries the images the text places (pandoc). Their images are not Markdown: they are
    // streamed to the asset intake instead of read into memory, and count against their own caps below.
    private static readonly string[] ImageSources = ["pandoc"];

    // One image: the asset upload's cap (AssetService.MaxUploadBytes). A larger image is skipped, not refused.
    public const long MaxImageBytes = 25L * 1024 * 1024;

    // All images of one import (the Players Guide places about 1.5 GB). The form reader spools them to disk.
    public const long MaxImageTotalBytes = 4L * 1024 * 1024 * 1024;

    // The request limit POST /api/imports sets: MaxRequestBytes plus the images.
    public const long MaxImportRequestBytes = MaxRequestBytes + MaxImageTotalBytes;

    public static bool TakesImages(string source) => ImageSources.Contains(source, StringComparer.Ordinal);

    private const int MaxPathLength = 400;

    public static int MaxUploadedFileBytes(string source) =>
        ArchiveSources.Contains(source, StringComparer.Ordinal) ? (int)MaxTotalBytes : MaxFileBytes;

    // A relative path with forward slashes and no empty, "." or ".." segment. Anything else (rooted, a drive letter,
    // a backslash, a control character) is refused for the whole upload.
    public static string CheckPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength
            || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
        {
            throw Refused(path);
        }
        var segments = path.Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.Trim().Length == 0))
        {
            throw Refused(path);
        }
        return path;
    }

    public static void CheckTotals(int fileCount, long totalBytes)
    {
        if (fileCount == 0)
        {
            throw ApiException.Validation("The folder is empty: pick a folder with Markdown files in it.");
        }
        if (fileCount > MaxFiles)
        {
            throw ApiException.Validation($"An import can hold at most {MaxFiles} files; this folder has {fileCount}.");
        }
        if (totalBytes > MaxTotalBytes)
        {
            throw ApiException.Validation($"An import can be at most {MaxTotalBytes / (1024 * 1024)} MB.");
        }
    }

    public static void CheckImageTotal(long imageBytes)
    {
        if (imageBytes > MaxImageTotalBytes)
        {
            throw ApiException.Validation($"The images of an import can be at most {MaxImageTotalBytes / (1024 * 1024 * 1024)} GB in all.");
        }
    }

    private static ApiException Refused(string? path) =>
        ApiException.Validation($"\"{path}\" is not a file name inside the folder; nothing was imported.");
}
