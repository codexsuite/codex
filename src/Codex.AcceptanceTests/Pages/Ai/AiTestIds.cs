namespace Codex.AcceptanceTests.Pages.Ai;

// Mirror of src/Codex.Client/src/features/ai/testIds.ts (prefix ai-).
public static class AiTestIds
{
    public const string Page = "ai-page";

    public const string Empty = "ai-empty";

    public const string List = "ai-list";

    public const string Error = "ai-error";

    public const string Connect = "ai-connect";

    public static string Row(Guid id) => $"ai-row-{id}";

    public static string RowKeyHint(Guid id) => $"ai-row-key-hint-{id}";

    public static string RowDefaultBadge(Guid id) => $"ai-row-default-badge-{id}";

    public static string RowModel(Guid id) => $"ai-row-model-{id}";

    public static string RowLastUsed(Guid id) => $"ai-row-last-used-{id}";

    public static string RowLastError(Guid id) => $"ai-row-last-error-{id}";

    public static string RowTest(Guid id) => $"ai-row-test-{id}";

    public static string RowTestResult(Guid id) => $"ai-row-test-result-{id}";

    public static string RowMakeDefault(Guid id) => $"ai-row-make-default-{id}";

    public static string RowChangeModel(Guid id) => $"ai-row-change-model-{id}";

    public static string RowReplaceKey(Guid id) => $"ai-row-replace-key-{id}";

    public static string RowRename(Guid id) => $"ai-row-rename-{id}";

    public static string RowRemove(Guid id) => $"ai-row-remove-{id}";

    public static string RowEditInput(Guid id) => $"ai-row-edit-input-{id}";

    public static string RowEditSave(Guid id) => $"ai-row-edit-save-{id}";

    public static string RowEditCancel(Guid id) => $"ai-row-edit-cancel-{id}";

    public static string RowRemoveConfirm(Guid id) => $"ai-row-remove-confirm-{id}";

    public const string Dialog = "ai-connect-dialog";

    public const string DialogClose = "ai-connect-close";

    public static string DialogKind(string kind) => $"ai-connect-kind-{kind}";

    public const string DialogLabel = "ai-connect-label";

    public const string DialogBaseUrl = "ai-connect-base-url";

    public const string DialogBaseUrlHint = "ai-connect-base-url-hint";

    public const string DialogApiKey = "ai-connect-api-key";

    public const string DialogTest = "ai-connect-test";

    public const string DialogTestResult = "ai-connect-test-result";

    public const string DialogModel = "ai-connect-model";

    public const string DialogSave = "ai-connect-save";

    public const string DialogError = "ai-connect-error";

    // Prompt templates section (J22).
    public const string Templates = "ai-templates";
    public static string TemplateRow(string templateId) => $"ai-template-row-{templateId}";
    public static string TemplateBuiltIn(string templateId) => $"ai-template-built-in-{templateId}";
    public static string TemplateEdit(string templateId) => $"ai-template-edit-{templateId}";
    public static string TemplateDuplicate(string templateId) => $"ai-template-duplicate-{templateId}";
    public static string TemplateDelete(string templateId) => $"ai-template-delete-{templateId}";
    public const string DuplicateName = "ai-duplicate-name";
    public const string DuplicateSave = "ai-duplicate-save";
    public const string DuplicateError = "ai-duplicate-error";
    public const string Editor = "ai-editor";
    public const string EditorName = "ai-editor-name";
    public const string EditorBody = "ai-editor-body";
    public const string EditorTokens = "ai-editor-tokens";
    public static string EditorVariable(string name) => $"ai-editor-variable-{name}";
    public const string EditorWarning = "ai-editor-warning";
    public const string EditorError = "ai-editor-error";
    public const string EditorSave = "ai-editor-save";
    public const string EditorCancel = "ai-editor-cancel";
}
