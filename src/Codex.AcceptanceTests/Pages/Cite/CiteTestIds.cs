namespace Codex.AcceptanceTests.Pages.Cite;

// Mirror of the cite ids in src/Codex.Client/src/features/write/testIds.ts (prefix write-cite-).
public static class CiteTestIds
{
    public const string Picker = "write-cite-picker";
    public const string Search = "write-cite-search";
    public const string NewSource = "write-cite-new-source";
    public const string Remove = "write-cite-remove";

    // The `/` menu item (prose/testIds.ts: prose-slash-item-<id>) that opens the picker.
    public const string SlashSourceReference = "prose-slash-item-source-reference";

    public static string Option(Guid sourceId) => $"write-cite-option-{sourceId}";

    // The shell's toast (src/editing/testIds.ts).
    public const string Toast = "editing-toast";
}
