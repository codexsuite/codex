namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// The mobile editor page's data-testid values (src/Codex.Client/src/mobileEditor/testIds.ts, prefix mobile-editor-), and the
// two prose menus it reuses (src/Codex.Client/src/prose/testIds.ts).
public static class MobileEditorTestIds
{
    public const string Root = "mobile-editor-root";

    public const string SlashMenu = "prose-slash-menu";

    public const string MentionPicker = "prose-mention-picker";

    public static string Scene(string sceneId) => $"mobile-editor-scene-{sceneId}";

    public static string SceneTitle(string sceneId) => $"mobile-editor-scene-title-{sceneId}";

    public static string SceneProse(string sceneId) => $"mobile-editor-scene-prose-{sceneId}";

    public static string SceneEditor(string sceneId) => $"mobile-editor-scene-editor-{sceneId}";

    public static string SceneCard(string sceneId) => $"mobile-editor-scene-card-{sceneId}";

    public static string SceneNumber(string sceneId) => $"mobile-editor-scene-number-{sceneId}";

    public static string SceneWords(string sceneId) => $"mobile-editor-scene-words-{sceneId}";

    public static string SceneSummary(string sceneId) => $"mobile-editor-scene-summary-{sceneId}";

    public static string SceneLabel(string sceneId, string label) => $"mobile-editor-scene-label-{sceneId}-{label}";

    public static string SceneChip(string sceneId, string entryId) => $"mobile-editor-scene-chip-{sceneId}-{entryId}";

    public static string Beat(string beatId) => $"mobile-editor-beat-{beatId}";

    public static string BeatInstruction(string beatId) => $"mobile-editor-beat-instruction-{beatId}";

    public static string BeatHidden(string beatId) => $"mobile-editor-beat-hidden-{beatId}";

    public static string BeatLength(string beatId) => $"mobile-editor-beat-length-{beatId}";

    public static string Addition(string additionId) => $"mobile-editor-addition-{additionId}";

    public static string MentionOption(string entryId) => $"prose-mention-option-{entryId}";

    public static string SlashItem(string itemId) => $"prose-slash-item-{itemId}";
}
