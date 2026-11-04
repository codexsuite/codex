namespace Codex.Application.Features.Print.Render;

// A PDF render's result. PdfPath is set only on success (Error null); the file lives in JobDirectory, which Dispose
// deletes, so a caller stores the PDF (as an asset) before disposing. Log is pandoc's then Tectonic's output and the
// TeX log, for the proof viewer's log and the overfull-box diagnostics.
public sealed class RenderedPdf(AssembledDocument document, string jobDirectory, string? pdfPath, string log, string? error, TimeSpan duration) : IDisposable
{
    public AssembledDocument Document { get; } = document;

    public string JobDirectory { get; } = jobDirectory;

    public string? PdfPath { get; } = pdfPath;

    public string Log { get; } = log;

    public string? Error { get; } = error;

    public TimeSpan Duration { get; } = duration;

    public bool Succeeded => Error is null;

    public void Dispose()
    {
        if (Directory.Exists(JobDirectory))
        {
            Directory.Delete(JobDirectory, recursive: true);
        }
    }
}
