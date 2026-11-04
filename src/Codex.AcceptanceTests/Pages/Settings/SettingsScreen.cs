namespace Codex.AcceptanceTests.Pages.Settings;

// The Settings screen: nav, Book, Series and Writing sections.
public sealed class SettingsScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(SettingsTestIds.Page);

    public ILocator NavItem(string section) => page.GetByTestId(SettingsTestIds.NavItem(section));

    public ILocator Card(string section) => page.GetByTestId(SettingsTestIds.Card(section));

    public ILocator Title => page.GetByTestId(SettingsTestIds.Title);

    // Sharing (#80): the "This book" list, with its people and its own Add people field.
    public ILocator SharingBookList => page.GetByTestId(SettingsTestIds.SharingBookList);

    public ILocator SharingPerson(string name) =>
        SharingBookList.GetByTestId(SettingsTestIds.SharingPerson).Filter(new() { HasText = name });

    public async Task ShareThisBookAsync(string email, string role)
    {
        await SharingBookList.GetByTestId(SettingsTestIds.SharingEmail).FillAsync(email);
        var match = SharingBookList.GetByTestId(SettingsTestIds.SharingMatch);
        await match.GetByTestId(SettingsTestIds.SharingMatchRole).SelectOptionAsync(role);
        await match.GetByTestId(SettingsTestIds.SharingAdd).ClickAsync();
    }

    public ILocator Author => page.GetByTestId(SettingsTestIds.Author);

    public ILocator StatusOption(string status) => page.GetByTestId(SettingsTestIds.StatusOption(status));

    public ILocator Target => page.GetByTestId(SettingsTestIds.Target);

    public ILocator ProgressText => page.GetByTestId(SettingsTestIds.ProgressText);

    public ILocator SeriesSelect => page.GetByTestId(SettingsTestIds.SeriesSelect);

    public ILocator NumberSelect => page.GetByTestId(SettingsTestIds.NumberSelect);

    public ILocator NumberLine => page.GetByTestId(SettingsTestIds.NumberLine);

    public ILocator OrderRow(string bookId) => page.GetByTestId(SettingsTestIds.OrderRow(bookId));

    public ILocator OrderWas(string bookId) => page.GetByTestId(SettingsTestIds.OrderWas(bookId));

    public ILocator MoveNote => page.GetByTestId(SettingsTestIds.MoveNote);

    public ILocator MoveReview => page.GetByTestId(SettingsTestIds.MoveReview);

    public ILocator MoveDialog => page.GetByTestId(SettingsTestIds.MoveDialog);

    public ILocator MoveFromTo => page.GetByTestId(SettingsTestIds.MoveFromTo);

    public ILocator MoveStep(string key) => page.GetByTestId(SettingsTestIds.MoveStep(key));

    public ILocator MoveWarning => page.GetByTestId(SettingsTestIds.MoveWarning);

    public ILocator MoveCancel => page.GetByTestId(SettingsTestIds.MoveCancel);

    public ILocator MoveConfirm => page.GetByTestId(SettingsTestIds.MoveConfirm);

    public ILocator Toast => page.GetByTestId(SettingsTestIds.Toast);

    public ILocator ToastUndo => page.GetByTestId(SettingsTestIds.ToastUndo);

    public ILocator QuoteLimit => page.GetByTestId(SettingsTestIds.QuoteLimit);

    public ILocator QuoteLimitError => page.GetByTestId(SettingsTestIds.QuoteLimitError);

    public ILocator ImpactSummary => page.GetByTestId(SettingsTestIds.ImpactSummary);

    public ILocator Publisher => page.GetByTestId(SettingsTestIds.Publisher);

    public ILocator PlannedYear => page.GetByTestId(SettingsTestIds.PlannedYear);

    public ILocator Letter => page.GetByTestId(SettingsTestIds.Letter);

    public ILocator LetterMissing => page.GetByTestId(SettingsTestIds.LetterMissing);

    public ILocator LayoutTemplate => page.GetByTestId(SettingsTestIds.LayoutTemplate);

    public ILocator LayoutGate => page.GetByTestId(SettingsTestIds.LayoutGate);

    public ILocator LayoutNone => page.GetByTestId(SettingsTestIds.LayoutNone);

    public ILocator ArchivedBar => page.GetByTestId(SettingsTestIds.ArchivedBar);

    public ILocator Duplicate => page.GetByTestId(SettingsTestIds.Duplicate);

    public ILocator DuplicateDialog => page.GetByTestId(SettingsTestIds.DuplicateDialog);

    public ILocator DuplicateTitle => page.GetByTestId(SettingsTestIds.DuplicateTitle);

    public ILocator DuplicateConfirm => page.GetByTestId(SettingsTestIds.DuplicateConfirm);

    public ILocator DuplicateDone => page.GetByTestId(SettingsTestIds.DuplicateDone);

    public ILocator ExportSource => page.GetByTestId(SettingsTestIds.ExportSource);

    public ILocator ExportStatus => page.GetByTestId(SettingsTestIds.ExportStatus);

    public ILocator ExportDownload => page.GetByTestId(SettingsTestIds.ExportDownload);

    public ILocator ExportNotes => page.GetByTestId(SettingsTestIds.ExportNotes);

    public ILocator ExportManuscript => page.GetByTestId(SettingsTestIds.ExportManuscript);

    public ILocator ManuscriptDialog => page.GetByTestId(SettingsTestIds.ManuscriptDialog);

    public ILocator ManuscriptFormat(string format) => page.GetByTestId(SettingsTestIds.ManuscriptFormat(format));

    public ILocator ManuscriptScope => page.GetByTestId(SettingsTestIds.ManuscriptScope);

    public ILocator ManuscriptChapter => page.GetByTestId(SettingsTestIds.ManuscriptChapter);

    public ILocator ManuscriptBeats => page.GetByTestId(SettingsTestIds.ManuscriptBeats);

    public ILocator ManuscriptSummaries => page.GetByTestId(SettingsTestIds.ManuscriptSummaries);

    public ILocator ManuscriptRisk => page.GetByTestId(SettingsTestIds.ManuscriptRisk);

    public ILocator ManuscriptBusy => page.GetByTestId(SettingsTestIds.ManuscriptBusy);

    public ILocator ManuscriptError => page.GetByTestId(SettingsTestIds.ManuscriptError);

    public ILocator ManuscriptSubmit => page.GetByTestId(SettingsTestIds.ManuscriptSubmit);

    public ILocator ManuscriptCancel => page.GetByTestId(SettingsTestIds.ManuscriptCancel);

    // The manuscript copies listed under Export manuscript, newest first.
    public ILocator ManuscriptJobs => page.GetByTestId(SettingsTestIds.ManuscriptJob);

    public ILocator ManuscriptJobStatus => page.GetByTestId(SettingsTestIds.ManuscriptJobStatus);

    public ILocator ManuscriptDownload => page.GetByTestId(SettingsTestIds.ManuscriptDownload);

    public ILocator ManuscriptJobError => page.GetByTestId(SettingsTestIds.ManuscriptJobError);

    public ILocator ManuscriptJobNotes => page.GetByTestId(SettingsTestIds.ManuscriptJobNotes);

    public ILocator ManuscriptRetry => page.GetByTestId(SettingsTestIds.ManuscriptRetry);

    public ILocator Archive => page.GetByTestId(SettingsTestIds.Archive);

    public ILocator ArchiveDialog => page.GetByTestId(SettingsTestIds.ArchiveDialog);

    public ILocator ArchiveConfirm => page.GetByTestId(SettingsTestIds.ArchiveConfirm);

    public ILocator Restore => page.GetByTestId(SettingsTestIds.Restore);

    public ILocator LifecycleError => page.GetByTestId(SettingsTestIds.LifecycleError);

    public ILocator Chip(string field) => page.GetByTestId(SettingsTestIds.ChipFor(field));

    public ILocator CitationStyleOption(string style) => page.GetByTestId(SettingsTestIds.CitationStyleOption(style));

    public ILocator LayoutTemplatePick => page.GetByTestId(SettingsTestIds.LayoutTemplatePick);

    public ILocator LayoutTrim(string trim) => page.GetByTestId(SettingsTestIds.LayoutTrim(trim));

    public ILocator LayoutChangelog => page.GetByTestId(SettingsTestIds.LayoutChangelog);

    public ILocator LayoutUpgradeApply => page.GetByTestId(SettingsTestIds.LayoutUpgradeApply);

    public ILocator LayoutForkName => page.GetByTestId(SettingsTestIds.LayoutForkName);

    public ILocator LayoutForkConfirm => page.GetByTestId(SettingsTestIds.LayoutForkConfirm);

    public ILocator LayoutTemplatesToggle => page.GetByTestId(SettingsTestIds.LayoutTemplatesToggle);

    public ILocator LayoutTemplateError => page.GetByTestId(SettingsTestIds.LayoutTemplateError);

    public ILocator LayoutTemplateCard(string slug) => page.GetByTestId(SettingsTestIds.LayoutTemplateCard(slug));

    public ILocator LayoutTemplateUse(string slug) => page.GetByTestId(SettingsTestIds.LayoutTemplateUse(slug));

    public ILocator LayoutTemplateFork(string slug) => page.GetByTestId(SettingsTestIds.LayoutTemplateFork(slug));

    public ILocator LayoutTemplateUploadVersion(string slug) => page.GetByTestId(SettingsTestIds.LayoutTemplateUploadVersion(slug));

    public ILocator PublishDialog => page.GetByTestId(SettingsTestIds.PublishDialog);

    public ILocator PublishMessage => page.GetByTestId(SettingsTestIds.PublishMessage);

    public ILocator PublishWarning => page.GetByTestId(SettingsTestIds.PublishWarning);

    public ILocator PublishConfirm => page.GetByTestId(SettingsTestIds.PublishConfirm);

    public ILocator PublishCancel => page.GetByTestId(SettingsTestIds.PublishCancel);

    public ILocator SnapshotsHeading => page.GetByTestId(SettingsTestIds.SnapshotsHeading);

    // Every snapshot row, and every Download link, whatever the job id.
    public ILocator SnapshotRows => page.GetByTestId(new System.Text.RegularExpressions.Regex("^settings-snapshot-[0-9a-f]{8}-"));

    public ILocator SnapshotDownloads => page.GetByTestId(new System.Text.RegularExpressions.Regex("^settings-snapshot-download-"));

    public ILocator NewEdition => page.GetByTestId(SettingsTestIds.NewEdition);

    public ILocator DuplicateCancel => page.GetByTestId(SettingsTestIds.DuplicateCancel);

    public ILocator DuplicateError => page.GetByTestId(SettingsTestIds.DuplicateError);
}
