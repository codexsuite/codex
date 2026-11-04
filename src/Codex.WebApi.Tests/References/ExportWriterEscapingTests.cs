using System.Text.Json;
using Codex.Application.Features.References;

namespace Codex.WebApi.Tests.References;

[TestFixture]
public class ExportWriterEscapingTests
{
    private static readonly JsonElement Item = JsonDocument.Parse("""
        {"id":"s1","type":"book","title":"Rock {and} Roll & \\ more\nsecond line","author":[{"family":"O'Neil","given":"A_B"}],"issued":{"date-parts":[[1999]]}}
        """).RootElement;

    [Test]
    public void BibTex_WhenValueHasSpecialCharacters_ShouldEscapeThem()
    {
        var text = BibTexWriter.Write([Item]);

        text.ShouldContain("{Rock \\{and\\} Roll \\& \\textbackslash{} more second line}");
        text.ShouldContain("author = {O'Neil, A\\_B}");
        text.ShouldNotContain("more\n");
    }

    [Test]
    public void Ris_WhenValueHasLineBreak_ShouldKeepItOnOneLine()
    {
        var text = RisWriter.Write([Item]);

        text.ShouldContain("TI  - Rock {and} Roll & \\ more second line\r\n");
        text.Split("\r\n").Count(l => l.StartsWith("ER  -", StringComparison.Ordinal)).ShouldBe(1);
    }
}
