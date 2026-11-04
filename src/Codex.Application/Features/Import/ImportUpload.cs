namespace Codex.Application.Features.Import;

// What the browser handed over: the source the author picked, the files, and the ones it left out or that were too
// large to read (listed as skipped without being read). A parser for a single-file source (Novelcrafter .zip, .docx)
// gets one file and unpacks it itself. Images are the image files of a source that places them (pandoc), kept out of
// Files so their bytes are only read when they are stored as assets.
public sealed record ImportUpload(
    string Source, IReadOnlyList<ImportFile> Files, IReadOnlyList<ImportSkippedItem> Omitted, IReadOnlyList<ImportImage>? Images = null)
{
    public ImportUpload(string source, IReadOnlyList<ImportFile> files)
        : this(source, files, [])
    {
    }
}
