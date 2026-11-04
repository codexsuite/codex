using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.Application.Features.Print;

// The effective dpi rule (#13): the image is scaled to fit inside the placement's box (TrimSizes.PlacedBox, in the
// template's PageLayout when one is given) keeping
// its aspect, so a tall image is limited by the box height, and dpi = pixels / placed inches. "low" is judged on the
// unrounded dpi against the target (300 Print, 150 Screen); the DTO shows it rounded to 0.1. The width is the
// original's, because the print rendition keeps the original's pixels up to 300 dpi at the largest placement.
public static class EffectiveDpi
{
    public const int PrintTarget = 300;
    public const int ScreenTarget = 150;

    public static AssetDpiDto Compute(int widthPx, int heightPx, string trim, int columns, string placement, int targetDpi = PrintTarget, PageLayout? layout = null)
    {
        if (targetDpi is not (PrintTarget or ScreenTarget))
        {
            throw ApiException.Validation($"Target dpi must be {PrintTarget} (print) or {ScreenTarget} (screen).");
        }

        var (boxWidth, boxHeight) = TrimSizes.PlacedBox(trim, columns, placement, layout);
        var placedWidth = Math.Min(boxWidth, boxHeight * widthPx / heightPx);
        var placedHeight = placedWidth * heightPx / widthPx;
        var dpi = widthPx / placedWidth;
        return new AssetDpiDto(
            trim.ToLowerInvariant(), columns, placement,
            Math.Round(placedWidth, 3), Math.Round(placedHeight, 3), Math.Round(dpi, 1), targetDpi,
            dpi >= targetDpi ? "ok" : "low");
    }
}
