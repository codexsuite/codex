namespace Codex.Application.Features.Print;

// One rendition ready to store: Purpose is web, proxy or print; Extension has no dot.
public sealed record RenditionFile(string Purpose, string Mime, string Extension, int WidthPx, int HeightPx, byte[] Content);
