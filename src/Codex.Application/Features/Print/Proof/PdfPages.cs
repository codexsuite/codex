using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Print.Proof;

// The page count of a PDF, read from the page tree's root (/Type /Pages … /Count n) without a PDF library. Tectonic
// keeps its objects in compressed object streams, so those are inflated and searched too. Null when no page tree is found.
public static partial class PdfPages
{
    public static int? Count(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var counts = Roots(text).ToList();
        foreach (Match stream in ObjectStream().Matches(text))
        {
            var inflated = Inflate(pdf, text, stream.Index);
            if (inflated is not null)
            {
                counts.AddRange(Roots(inflated));
            }
        }

        return counts.Count == 0 ? null : counts.Max();
    }

    private static IEnumerable<int> Roots(string text) =>
        Pages().Matches(text).Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));

    // The stream that follows the dictionary at `from`: between "stream" + EOL and "endstream". Null if it is not valid zlib.
    private static string? Inflate(byte[] pdf, string text, int from)
    {
        var keyword = text.IndexOf("stream", from, StringComparison.Ordinal);
        if (keyword < 0)
        {
            return null;
        }

        var start = keyword + "stream".Length;
        start += text[start] == '\r' ? 2 : 1;
        var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        try
        {
            using var zlib = new ZLibStream(new MemoryStream(pdf, start, end - start), CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"/Type\s*/ObjStm")]
    private static partial Regex ObjectStream();

    [GeneratedRegex(@"/Type\s*/Pages\b[^>]*?/Count\s+(?<n>\d+)|/Count\s+(?<n>\d+)[^>]*?/Type\s*/Pages\b")]
    private static partial Regex Pages();
}
