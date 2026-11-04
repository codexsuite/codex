namespace Codex.AcceptanceTests.Pages.Images;

// Mirror of src/Codex.Client/src/images/testIds.ts (prefix images-): the image picker Write's figure dialogs and Plan's
// opener dialog share.
public static class ImagesTestIds
{
    public const string Picker = "images-picker";
    public const string UploadInput = "images-upload-input";
    public const string Selected = "images-selected";
    public const string SelectedDpi = "images-selected-dpi";

    public static string Option(Guid assetId) => $"images-option-{assetId}";
}
