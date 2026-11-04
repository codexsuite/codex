using System.Buffers.Binary;
using System.IO.Compression;
using SkiaSharp;

namespace Codex.WebApi.Tests.Print;

// Synthetic images for the asset tests, encoded in memory (no files, no network).
public static class TestImages
{
    // A smooth colour gradient with noise: many distinct colours, so it is not line art.
    public static byte[] Photo(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = (byte)(x * 255 / width);
                pixels[at + 1] = (byte)(y * 255 / height);
                pixels[at + 2] = (byte)random.Next(256);
                pixels[at + 3] = 255;
            }
        }
        return Encode(bitmap, format);
    }

    // Black ink on white: two colours.
    public static byte[] LineArt(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                var ink = ((x / 8) + (y / 8)) % 2 == 0 ? 0 : 255;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)ink;
                pixels[at + 3] = 255;
            }
        }
        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    // A valid 1-bit grayscale PNG of the given size whose rows are all zero: a few KB on the wire, so a pixel-cap test
    // needs no big upload and no pixel work here.
    public static byte[] BlankPng(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // bit depth; colour type 0 (grayscale), no interlace
        WriteChunk(png, "IHDR", header);

        var row = new byte[1 + ((width + 7) / 8)]; // filter byte 0, then zero pixels
        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }
        WriteChunk(png, "IDAT", deflated.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        data.CopyTo(body, 4);
        stream.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    public static SKBitmap Decode(byte[] content) => SKBitmap.Decode(content);

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
