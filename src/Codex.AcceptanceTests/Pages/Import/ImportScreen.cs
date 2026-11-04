using Codex.AcceptanceTests.Pages.Library;

namespace Codex.AcceptanceTests.Pages.Import;

// The Library's Import dialog (both steps) and the Plan's import banner.
public sealed class ImportScreen(IPage page)
{
    public ILocator OpenButton => page.GetByTestId(LibraryTestIds.Import);

    public ILocator Dialog => page.GetByTestId(LibraryTestIds.ImportDialog);

    public ILocator FileInput => page.GetByTestId(ImportTestIds.LibraryImportFile);

    public ILocator Next => page.GetByTestId(ImportTestIds.LibraryImportNext);

    public ILocator Preview => page.GetByTestId(ImportTestIds.Preview);

    public ILocator PreviewTitle => page.GetByTestId(ImportTestIds.PreviewTitle);

    public ILocator SkippedRows => page.GetByTestId(ImportTestIds.SkippedRow);

    public ILocator Run => page.GetByTestId(ImportTestIds.Run);

    public ILocator Banner => page.GetByTestId(ImportTestIds.Banner);

    public ILocator BannerText => page.GetByTestId(ImportTestIds.BannerText);

    public ILocator BannerSkipped => page.GetByTestId(ImportTestIds.BannerSkipped);

    public ILocator Undo => page.GetByTestId(ImportTestIds.Undo);

    public ILocator UndoConfirm => page.GetByTestId(ImportTestIds.UndoConfirm);

    public ILocator UndoConfirmDelete => page.GetByTestId(ImportTestIds.UndoConfirmDelete);

    public ILocator Dismiss => page.GetByTestId(ImportTestIds.Dismiss);

    public ILocator BannerSkippedRows => page.GetByTestId(ImportTestIds.BannerSkippedRow);

    public ILocator Source(string source) => page.GetByTestId(ImportTestIds.LibraryImportSource(source));

    public ILocator PreviewRow(string item) => page.GetByTestId(ImportTestIds.PreviewRow(item));

    // Opens the dialog from the Library, picks the Markdown folder source and the folder, and goes to step 2.
    public async Task PickFolderAsync(string folder)
    {
        await Assertions.Expect(OpenButton).ToBeVisibleAsync();
        await OpenButton.ClickAsync();
        await Source("markdown").ClickAsync();
        await FileInput.SetInputFilesAsync(folder);
        await Next.ClickAsync();
    }

    // As PickFolderAsync, for a single-file source (novelcrafter, docx): the file's bytes under its name.
    public async Task PickFileAsync(string source, string fileName, string mimeType, byte[] content)
    {
        await Assertions.Expect(OpenButton).ToBeVisibleAsync();
        await OpenButton.ClickAsync();
        await Source(source).ClickAsync();
        await FileInput.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = mimeType, Buffer = content });
        await Next.ClickAsync();
    }
}
