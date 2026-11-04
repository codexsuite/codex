using System.Text.RegularExpressions;

namespace Codex.AcceptanceTests.Pages.References;

// The References screen: summary strip, sources table, bibliography and the export menu.
public sealed class ReferencesScreen(IPage page)
{
    private static readonly Regex RowIdPattern = new("^refs-row-[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$");

    public ILocator Root => page.GetByTestId(ReferencesTestIds.Page);

    public ILocator ClearedText => page.GetByTestId(ReferencesTestIds.ClearedText);

    public ILocator Gate => page.GetByTestId(ReferencesTestIds.Gate);

    public ILocator Chip(string filter) => page.GetByTestId(ReferencesTestIds.Chip(filter));

    public ILocator ChipCount(string filter) => page.GetByTestId(ReferencesTestIds.ChipCount(filter));

    public ILocator Row(Guid sourceId) => page.GetByTestId(ReferencesTestIds.Row(sourceId));

    // Every source row, whatever its id.
    public ILocator RemindersChip => page.GetByTestId(ReferencesTestIds.RemindersChip);

    public ILocator RowReminder(Guid sourceId) => Row(sourceId).GetByTestId(ReferencesTestIds.RowReminder);

    public ILocator Rows => page.GetByTestId(RowIdPattern);

    public ILocator RowStatuses => page.GetByTestId(ReferencesTestIds.RowStatus);

    public ILocator ViewBibliography => page.GetByTestId(ReferencesTestIds.ViewBibliography);

    public ILocator BibliographyEntries => page.GetByTestId(ReferencesTestIds.BibEntry);

    public ILocator BibliographyCredits => page.GetByTestId(ReferencesTestIds.BibCredit);

    public ILocator ExportToggle => page.GetByTestId(ReferencesTestIds.ExportToggle);

    public ILocator ExportLink(string format) => page.GetByTestId(ReferencesTestIds.ExportLink(format));

    public async Task<IDownload> ExportAsync(string format)
    {
        if (await ExportLink(format).CountAsync() == 0)
        {
            // Export lives on the Bibliography view.
            if (await ExportToggle.CountAsync() == 0)
            {
                await ViewBibliography.ClickAsync();
            }

            await ExportToggle.ClickAsync();
        }

        return await page.RunAndWaitForDownloadAsync(() => ExportLink(format).ClickAsync());
    }
}
