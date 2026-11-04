namespace Codex.TestSupport;

// The shell's data-testid registry (client side: src/Codex.Client/src/testIds.ts, kept identical and pinned by
// TestIdRegistryTests). Selectors are data-testid only. Lanes do NOT add ids here: each lane keeps its own in
// features/<area>/testIds.ts (client) and its own TestIds file with its prefix (library-, plan-, write-, kb-,
// refs-, settings-). Shell ids are prefixed shell- (except app-shell).
public static class TestIds
{
    public static class Shell
    {
        public const string AppShell = "app-shell";
        public const string LibraryPage = "shell-library-page";
        public const string Shelf = "shell-shelf";
        public const string ListView = "shell-list-view";
        public const string ViewToggle = "shell-view-toggle";
        public const string SearchBox = "shell-search-box";
        public const string SortControl = "shell-sort-control";
        public const string GroupControl = "shell-group-control";
        public const string SeriesScreen = "shell-series-screen";
        public const string BookBar = "shell-book-bar";
        public const string TabPlan = "shell-tab-plan";
        public const string TabWrite = "shell-tab-write";
        public const string TabReferences = "shell-tab-references";
        public const string TabLayout = "shell-tab-layout";
        public const string TabSettings = "shell-tab-settings";
        public const string BackLink = "shell-back-link";
        public const string SaveStatusSlot = "shell-slot-save-status";
        public const string BadgeSlot = "shell-slot-badge";
        public const string ScopeSlot = "shell-slot-scope";
        public const string FormatSlot = "shell-slot-format";
        public const string FocusSlot = "shell-slot-focus";
        public const string PaletteSlot = "shell-slot-palette";
        public const string CommandPalette = "shell-command-palette";
        public const string KbSidebar = "shell-kb-sidebar";
        public const string KbList = "shell-kb-list";
        public const string ErrorUnknownBook = "shell-error-unknown-book";
        public const string ErrorBadLink = "shell-error-bad-link";
        public const string RoleChip = "shell-role-chip";

        public static string Tile(string bookId) => $"shell-tile-{bookId}";

        public static string ListRow(string bookId) => $"shell-list-row-{bookId}";

        public static string SeriesStack(string seriesId) => $"shell-series-stack-{seriesId}";

        public static string KbItem(string entryId) => $"shell-kb-item-{entryId}";

        public static string LanePlaceholder(string lane) => $"shell-lane-{lane}";
    }
}
