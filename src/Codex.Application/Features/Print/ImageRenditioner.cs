using SkiaSharp;

namespace Codex.Application.Features.Print;

// Pure image work for the rendition worker and the upload check; no database, no storage.
//   web    WebP, longest edge at most 1600 px (Quick preview)
//   proxy  JPEG at 150 dpi of the largest placed edge (proofs)
//   print  300 dpi of the largest placed edge; PNG when the source is PNG line art (64 colours or fewer), else JPEG
// The largest placed edge is 17.5 in (a Letter spread with bleed), applied to both
// dimensions. Nothing is ever upscaled, so a small source keeps its own pixels in every rendition, and effective dpi is judged on the original (EffectiveDpi).
public static class ImageRenditioner
{
    public const int WebLongestEdgePx = 1600;
    public const long MaxPixels = 64_000_000;
    public const double LargestPlacedEdgeIn = 17.5;
    public const int ProxyDpi = 150;
    public const int PrintDpi = 300;
    public const int WebQuality = 80;
    public const int JpegQuality = 90;
    public const int LineArtMaxColours = 64;

    // Null when the bytes are not a PNG, JPEG or WebP image.
    public static ImageProbe? Probe(byte[] content)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        return codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => new ImageProbe("image/png", "png", codec.Info.Width, codec.Info.Height),
            SKEncodedImageFormat.Jpeg => new ImageProbe("image/jpeg", "jpg", codec.Info.Width, codec.Info.Height),
            SKEncodedImageFormat.Webp => new ImageProbe("image/webp", "webp", codec.Info.Width, codec.Info.Height),
            _ => null,
        };
    }

    // Re-checks the pixel cap (a requeue, a duplicated book or a future import never went through the upload check).
    public static IReadOnlyList<RenditionFile> Render(byte[] content)
    {
        var probe = Probe(content) ?? throw new InvalidOperationException("The original is not a PNG, JPEG or WebP image.");
        if ((long)probe.WidthPx * probe.HeightPx > MaxPixels)
        {
            throw new InvalidOperationException($"The original is over {MaxPixels / 1_000_000} megapixels.");
        }

        var webScale = Math.Min(1.0, (double)WebLongestEdgePx / Math.Max(probe.WidthPx, probe.HeightPx));
        var renditions = new List<RenditionFile>(3);

        // The web rendition decodes at a reduced size where the format allows (JPEG, WebP), so a big original is
        // not held at full size for it; the full decode happens after this one is released.
        using (var small = Decode(content, webScale))
        {
            small.SetImmutable();
            using var smallImage = SKImage.FromBitmap(small);
            var webWidth = Math.Max(1, (int)Math.Round(probe.WidthPx * webScale));
            renditions.Add(Encode(smallImage, "web", SKEncodedImageFormat.Webp, "image/webp", "webp", (double)webWidth / smallImage.Width));
        }

        using var bitmap = Decode(content, 1.0);
        var printLine = probe.Mime == "image/png" && IsLineArt(bitmap);
        bitmap.SetImmutable(); // FromBitmap then shares the pixels instead of copying them
        using var image = SKImage.FromBitmap(bitmap);
        renditions.Add(Encode(image, "proxy", SKEncodedImageFormat.Jpeg, "image/jpeg", "jpg", FitScale(probe, ProxyDpi)));
        renditions.Add(printLine
            ? Encode(image, "print", SKEncodedImageFormat.Png, "image/png", "png", FitScale(probe, PrintDpi))
            : Encode(image, "print", SKEncodedImageFormat.Jpeg, "image/jpeg", "jpg", FitScale(probe, PrintDpi)));
        return renditions;
    }

    private static SKBitmap Decode(byte[] content, double scale)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("The original could not be decoded.");
        var size = scale < 1 ? codec.GetScaledDimensions((float)scale) : codec.Info.Size;
        return SKBitmap.Decode(codec, new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
               ?? throw new InvalidOperationException("The original could not be decoded.");
    }

    // Scale that keeps both dimensions within dpi * LargestPlacedEdgeIn; never above 1.
    private static double FitScale(ImageProbe probe, int dpi)
        => Math.Min(1.0, dpi * LargestPlacedEdgeIn / Math.Max(probe.WidthPx, probe.HeightPx));

    private static RenditionFile Encode(SKImage image, string purpose, SKEncodedImageFormat format, string mime, string extension, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        // JPEG has no alpha: flatten onto white so transparent art does not go black.
        canvas.Clear(format == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
        canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var rendered = surface.Snapshot();
        using var encoded = rendered.Encode(format, format == SKEncodedImageFormat.Webp ? WebQuality : JpegQuality);
        return new RenditionFile(purpose, mime, extension, width, height, encoded.ToArray());
    }

    // Few distinct colours (flat inks, no photographic gradient): keep PNG so edges stay crisp.
    private static bool IsLineArt(SKBitmap bitmap)
    {
        var colours = new HashSet<uint>();
        foreach (var pixel in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bitmap.GetPixelSpan()))
        {
            if (colours.Add(pixel) && colours.Count > LineArtMaxColours)
            {
                return false;
            }
        }
        return true;
    }
}
