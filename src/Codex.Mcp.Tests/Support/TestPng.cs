using SkiaSharp;

namespace Codex.Mcp.Tests.Support;

// A small PNG made in memory for the image tools (#65): a gradient, so it decodes and has many colours.
public static class TestPng
{
    public static byte[] Make(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / height), 128));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Writes it to a fresh file under the test's temp folder and returns the absolute path.
    public static string Write(string name, int width, int height)
    {
        var folder = Path.Combine(Path.GetTempPath(), "codex-mcp-tests-images", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Make(width, height));
        return path;
    }
}
