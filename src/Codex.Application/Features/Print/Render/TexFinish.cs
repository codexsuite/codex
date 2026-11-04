using System.Globalization;
using System.Text.RegularExpressions;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Print.Render;

// Applies a PrintFinish to the LaTeX pandoc wrote from a template's wrapper. Bleed and crop marks rewrite the memoir
// stock-size lines the print templates declare (the stock page grows by the bleed, the trimmed page stays the trim,
// memoir places the text block inside the trims); a template that does not use memoir's stock size cannot take a
// bleed, which is a 400. The watermark uses the kernel's shipout hook and the standard color and graphicx packages,
// so it needs nothing beyond what the sandbox's offline Tectonic cache already holds for the templates.
public static partial class TexFinish
{
    [GeneratedRegex(@"\\setstocksize\{([0-9.]+)in\}\{([0-9.]+)in\}[ \t]*\r?\n[ \t]*\\settrimmedsize\{\\stockheight\}\{\\stockwidth\}\{\*\}")]
    private static partial Regex Stock();

    private const string Watermark =
        "\\usepackage{color}\\usepackage{graphicx}\n" +
        "\\AddToHook{shipout/background}{\\put(0.5\\paperwidth,-0.5\\paperheight){\\makebox(0,0){\\rotatebox{50}{\\scalebox{9}{\\textcolor[gray]{0.8}{DRAFT}}}}}}\n";

    public static string Apply(string tex, PrintFinish finish, string templateName)
    {
        if (finish.IsPlain)
        {
            return tex;
        }

        var preamble = new List<string>();
        if (finish.BleedInches > 0)
        {
            var match = Stock().Match(tex);
            if (!match.Success)
            {
                throw ApiException.Validation($"The template {templateName} cannot add a bleed: it does not set a stock size.");
            }

            var height = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var width = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var bleed = finish.BleedInches;
            tex = string.Concat(
                tex.AsSpan(0, match.Index),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"\\setstocksize{{{height + 2 * bleed:0.####}in}}{{{width + 2 * bleed:0.####}in}}\n\\settrimmedsize{{{height}in}}{{{width}in}}{{*}}\n\\settrims{{{bleed:0.####}in}}{{{bleed:0.####}in}}"),
                tex.AsSpan(match.Index + match.Length));
            if (finish.CropMarks)
            {
                preamble.Add("\\trimLmarks");
            }
        }
        else if (finish.CropMarks)
        {
            throw ApiException.Validation("Crop marks need a bleed.");
        }

        if (finish.Draft)
        {
            preamble.Add(Watermark.TrimEnd('\n'));
        }

        var begin = tex.IndexOf("\\begin{document}", StringComparison.Ordinal);
        if (begin < 0)
        {
            throw ApiException.Validation($"The template {templateName} has no document body to finish.");
        }

        return tex.Insert(begin, string.Join("\n", preamble) + "\n");
    }
}
