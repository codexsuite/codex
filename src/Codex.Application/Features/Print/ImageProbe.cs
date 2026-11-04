namespace Codex.Application.Features.Print;

// What an upload decodes as: the sniffed type (never the declared one) and pixel size.
public sealed record ImageProbe(string Mime, string Extension, int WidthPx, int HeightPx);
