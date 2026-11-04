namespace Codex.Application.Features.Print.Render;

// Pdf places local image files (the job folder's images/); Html points images at the asset rendition URLs.
public enum RenderTarget
{
    Pdf,
    Html,
}
