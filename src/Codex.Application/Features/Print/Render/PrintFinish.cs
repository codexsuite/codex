namespace Codex.Application.Features.Print.Render;

// How a print export differs from the template's trimmed page: BleedInches of bleed on every side (the stock page is
// larger than the trim), crop marks outside the trim, and a DRAFT watermark on every page.
public sealed record PrintFinish(double BleedInches, bool CropMarks, bool Draft)
{
    public bool IsPlain => BleedInches <= 0 && !CropMarks && !Draft;
}
