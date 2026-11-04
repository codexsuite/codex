namespace Codex.Application.Features.Print.Render;

// PdfPath is set only on success (Error null). Log is Tectonic's output then the TeX log (overfull boxes for preflight).
public sealed record TectonicResult(string? PdfPath, string Log, string? Error, TimeSpan Duration)
{
    public bool Succeeded => Error is null;
}
