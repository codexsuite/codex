namespace Codex.TestSupport.Fixtures;

// The old Product Engineering manuscript as the importers receive it (issue #16: "Product Engineering (v1) stands for an
// import of the old Novelcrafter export"), small enough to read: a Novelcrafter-style zip made from product-engineering-v1/
// (2 acts, 3 chapters, 4 scenes, 3 Codex entries with KSUIDs, and one name a scene uses that the Codex lacks) and the same book as a Word document.
public static class ProductEngineeringV1
{
    public const string GoodhartKsuid = "0ujsswThIGTUYm2K8FjOOfXtY1K";
    public const string BeckKsuid = "0ujtsYcgvSTl8PAuAdqWYSMnLOv";
    public const string FeatureFactoryKsuid = "0ujzPyRiIAffKhBux4PvQdDqMHY";

    public static string Folder => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "product-engineering-v1");

    public static byte[] NovelcrafterZip() => TestZip.FromFolder(Folder, "product-engineering-v1");

    // Title and subtitle, a front-matter paragraph, then three chapters: Preface, one with two headed scenes, and one
    // split into two scenes by a section break; an image and a comment, which are not imported.
    public static byte[] Docx() => new DocxBuilder()
        .Title("Product Engineering")
        .Subtitle("Shipping the right thing")
        .Paragraph("Draft 3. Not for circulation.")
        .Heading(1, "Preface")
        .Runs(("For ten years I watched good teams ship the wrong thing, ", false, false), ("on time", false, true), (".", false, false))
        .Runs(("This book is about the ", false, false), ("other", true, false), (" way to work.", false, false))
        .Heading(1, "A Brief History of Software Practice")
        .Heading(2, "The beginnings in Agile")
        .Paragraph("Kent Beck wrote down what a small team already did.")
        .Image()
        .Heading(2, "Feature factories")
        .Paragraph("Measuring output because outcomes are hard.")
        .Heading(1, "Owning Outcomes")
        .Paragraph("Engineers belong in discovery.")
        .SectionBreak()
        .Paragraph("When a measure becomes a target, it ceases to be a good measure.")
        .Comment("Check the quote.")
        .Build();
}
