namespace Codex.AcceptanceTests.Pages.References;

// The unflagged-quotes inbox under the summary strip: the count button and the list it opens.
public sealed class QuoteFlagsInbox(IPage page)
{
    public ILocator Button => page.GetByTestId(ReferencesTestIds.FlagInbox);

    public ILocator Panel => page.GetByTestId(ReferencesTestIds.FlagPanel);

    public ILocator Item(Guid flagId) => page.GetByTestId(ReferencesTestIds.FlagItem(flagId));

    public ILocator Excerpts => page.GetByTestId(ReferencesTestIds.FlagExcerpt);

    public ILocator Scene => page.GetByTestId(ReferencesTestIds.FlagScene);

    public ILocator NotQuote(Guid flagId) => Item(flagId).GetByTestId(ReferencesTestIds.FlagNotQuote);

    public ILocator Empty => page.GetByTestId(ReferencesTestIds.FlagEmpty);

    public ILocator Cite(Guid flagId) => Item(flagId).GetByTestId(ReferencesTestIds.FlagCite);
}
