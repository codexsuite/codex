using Codex.AcceptanceTests.Pages.References;

namespace Codex.AcceptanceTests.Pages.Cite;

// The References side of citing from Write: Add source's return note, the return bar, Paraphrase with AI on a use,
// and the toast each hand-off ends with.
public sealed class CiteHandOffPage(IPage page)
{
    public ILocator ReturnNote => page.GetByTestId(ReferencesTestIds.AddReturnNote);

    public ILocator ReturnBar => page.GetByTestId(ReferencesTestIds.ReturnBar);

    public ILocator ReturnCite => page.GetByTestId(ReferencesTestIds.ReturnCite);

    public ILocator ReturnCancel => page.GetByTestId(ReferencesTestIds.ReturnCancel);

    public ILocator Paraphrase(Guid useId) => page.GetByTestId(ReferencesTestIds.UseParaphrase(useId));

    public ILocator Toasts => page.GetByTestId(CiteTestIds.Toast);
}
