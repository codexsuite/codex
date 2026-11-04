namespace Codex.Application.Features.Print.Templates;

public sealed class ManifestPage
{
    public int Columns { get; set; } = 1;

    public double ColumnGap { get; set; }

    public ManifestMargins? Margins { get; set; }
}
