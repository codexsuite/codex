namespace Codex.AcceptanceTests.Pages.Library;

// Mirror of src/Codex.Client/src/features/library/testIds.ts (prefix library-).
public static class LibraryTestIds
{
    public const string Cover = "library-cover";
    public const string UnknownCover = "library-unknown-cover";
    public const string StackCount = "library-stack-count";
    public const string Empty = "library-empty";
    public const string NoResults = "library-no-results";
    public const string ClearSearch = "library-clear-search";
    public const string LoadError = "library-load-error";
    public const string SeriesName = "library-series-name";
    public const string SeriesBookCount = "library-series-book-count";
    public const string StackGroup = "library-stack-group";
    public const string SharedBy = "library-shared-by";
    public const string SeriesShared = "library-series-shared";
    public const string RoleChip = "library-role-chip";
    public const string PartlySharedNote = "library-partly-shared-note";
    public const string StandaloneGroup = "library-standalone-group";
    public const string ViewGrid = "library-view-grid";
    public const string ViewList = "library-view-list";
    public const string GroupRow = "library-group-row";
    public const string StatusDot = "library-status-dot";
    public const string Progress = "library-progress";
    public const string EditedSincePublication = "library-edited-since-publication";

    public const string SeriesBreadcrumb = "library-series-breadcrumb";
    public const string SeriesTitle = "library-series-title";
    public const string SeriesStatBooks = "library-series-stat-books";
    public const string SeriesStatWords = "library-series-stat-words";
    public const string SeriesStatPublished = "library-series-stat-published";
    public const string SeriesStatLastEdit = "library-series-stat-last-edit";
    public const string SeriesSettingsButton = "library-series-settings";
    public const string SeriesAddBook = "library-series-add-book";
    public const string SeriesAddTile = "library-series-add-tile";
    public const string SeriesDialog = "library-series-dialog";
    public const string SeriesNameInput = "library-series-name-input";
    public const string SeriesDescriptionInput = "library-series-description-input";
    public const string SeriesNameError = "library-series-name-error";
    public const string SeriesSaveError = "library-series-save-error";
    public const string SeriesSave = "library-series-save";
    public const string SeriesCancel = "library-series-cancel";

    public const string NewBook = "library-new-book";
    public const string Import = "library-import";
    public const string NewBookDialog = "library-new-book-dialog";
    public const string NewBookTitle = "library-new-book-title";
    public const string NewBookTitleError = "library-new-book-title-error";
    public const string NewBookSeries = "library-new-book-series";
    public const string NewBookNumber = "library-new-book-number";
    public const string NewBookTarget = "library-new-book-target";
    public const string NewBookError = "library-new-book-error";
    public const string NewBookCreate = "library-new-book-create";
    public const string NewBookCancel = "library-new-book-cancel";
    public const string ImportDialog = "library-import-dialog";
    public const string PaletteInput = "library-palette-input";
    public const string PaletteList = "library-palette-list";
    public const string PaletteEmpty = "library-palette-empty";

    public static string PaletteBook(string bookId) => $"library-palette-book-{bookId}";

    public static string PaletteSeries(string seriesId) => $"library-palette-series-{seriesId}";

    public static string PaletteAction(string action) => $"library-palette-action-{action}";

    public static string SeriesOrderRow(string bookId) => $"library-series-order-row-{bookId}";

    public static string SeriesOrderNumber(string bookId) => $"library-series-order-number-{bookId}";

    public static string SeriesGrip(string bookId) => $"library-series-grip-{bookId}";

    public const string StatusFilter = "library-status-filter";
    public const string AddTile = "library-add-tile";
    public const string CoverAuthor = "library-cover-author";

    public const string DeleteBookDialog = "library-delete-book-dialog";
    public const string DeleteBookImpact = "library-delete-book-impact";
    public const string DeleteBookExport = "library-delete-book-export";
    public const string DeleteBookTitle = "library-delete-book-title";
    public const string DeleteBookConfirm = "library-delete-book-confirm";
    public const string DeleteBookCancel = "library-delete-book-cancel";
    public const string NewSeries = "library-new-series";
    public const string NewSeriesDialog = "library-new-series-dialog";
    public const string NewSeriesName = "library-series-name-field";
    public const string NewSeriesNameError = "library-series-name-error";
    public const string NewSeriesNotice = "library-new-series-notice";
    public const string NewSeriesPicked = "library-new-series-picked";
    public const string NewSeriesCreate = "library-new-series-create";
    public const string NewSeriesCancel = "library-new-series-cancel";
    public const string SeriesDeleteButton = "library-series-delete";
    public const string SeriesDeleteDialog = "library-series-delete-dialog";
    public const string SeriesDeleteImpact = "library-series-delete-impact";
    public const string SeriesDeleteConfirm = "library-series-delete-confirm";
    public const string SeriesDeleteCancel = "library-series-delete-cancel";

    public static string RestoreBook(string bookId) => $"library-restore-{bookId}";

    public static string BookMenu(string bookId) => $"library-book-menu-{bookId}";

    public static string DeleteBookItem(string bookId) => $"library-delete-book-{bookId}";

    public static string NewSeriesBook(string bookId) => $"library-new-series-book-{bookId}";

    public static string SeriesMoveUp(string bookId) => $"library-series-move-up-{bookId}";
}
