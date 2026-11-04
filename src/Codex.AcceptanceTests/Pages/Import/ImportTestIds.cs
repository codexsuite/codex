namespace Codex.AcceptanceTests.Pages.Import;

// Mirror of src/Codex.Client/src/features/library/importTestIds.ts (prefix import-), plus the step-1 ids of
// features/library/testIds.ts the Import dialog uses that LibraryTestIds.cs does not carry.
public static class ImportTestIds
{
    public const string Preview = "import-preview";
    public const string PreviewTitle = "import-preview-title";
    public const string Skipped = "import-skipped";
    public const string SkippedRow = "import-skipped-row";
    public const string PreviewError = "import-preview-error";
    public const string Loading = "import-loading";
    public const string Run = "import-run";
    public const string Banner = "import-banner";
    public const string BannerText = "import-banner-text";
    public const string BannerSkipped = "import-banner-skipped";
    public const string Undo = "import-undo";
    public const string Dismiss = "import-dismiss";
    public const string BannerError = "import-banner-error";
    public const string SelectionError = "import-selection-error";
    public const string RunError = "import-run-error";
    public const string BannerSkippedRow = "import-banner-skipped-row";
    public const string UndoConfirm = "import-undo-confirm";
    public const string UndoConfirmDelete = "import-undo-confirm-delete";
    public const string UndoConfirmCancel = "import-undo-confirm-cancel";

    public const string LibraryImportFile = "library-import-file";
    public const string LibraryImportNext = "library-import-next";

    // By the row's item, lower-cased with dashes: "Scene beats" -> import-preview-row-scene-beats.
    public static string PreviewRow(string item) => $"import-preview-row-{item.ToLowerInvariant().Replace(' ', '-')}";

    public static string LibraryImportSource(string source) => $"library-import-source-{source}";
}
