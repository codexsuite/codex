using System.Globalization;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.Application.Features.Print.Export;

// An ExportRequest's validated form: what the job row stores in export_job.settings and what the worker applies.
public sealed record ExportSettings(string Bleed, bool CropMarks, string Quality, string Format, bool Draft)
{
    public const string None = "none";

    private static readonly Dictionary<string, double> Bleeds = new(StringComparer.Ordinal)
    {
        [None] = 0,
        ["3mm"] = 3 / 25.4,
        ["0.125in"] = 0.125,
    };

    public double BleedInches => Bleeds[Bleed];

    // "print" places the 300 dpi renditions, "screen" the 150 dpi proxies.
    public string ImagePurpose => Quality == "print" ? "print" : "proxy";

    public int TargetDpi => Quality == "print" ? EffectiveDpi.PrintTarget : EffectiveDpi.ScreenTarget;

    public static ExportSettings From(ExportRequest request)
    {
        if (!Bleeds.ContainsKey(request.Bleed))
        {
            throw ApiException.Validation($"The bleed must be {string.Join(", ", Bleeds.Keys)}, not '{request.Bleed}'.");
        }

        if (request.Quality is not ("print" or "screen"))
        {
            throw ApiException.Validation("The image quality must be print (300 dpi) or screen (150 dpi).");
        }

        if (request.Format != "pdf")
        {
            throw ApiException.Validation("The format must be pdf.");
        }

        if (request.CropMarks && request.Bleed == None)
        {
            throw ApiException.Validation("Crop marks need a bleed.");
        }

        return new ExportSettings(request.Bleed, request.CropMarks, request.Quality, request.Format, request.Draft);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Bleed}/{Quality}");
}
