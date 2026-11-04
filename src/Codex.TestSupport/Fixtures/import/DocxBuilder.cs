using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Codex.TestSupport.Fixtures;

// A .docx written with the Open XML SDK in test code: Title, Subtitle and Heading styles by their built-in names, plain and
// formatted paragraphs, section breaks, and the parts the importer lists as skipped (an image, a comment).
public sealed class DocxBuilder
{
    private readonly List<OpenXmlElement> _blocks = [];
    private string? _comment;

    public DocxBuilder Title(string text) => Styled("Title", text);

    public DocxBuilder Subtitle(string text) => Styled("Subtitle", text);

    public DocxBuilder Heading(int level, string text) => Styled("Heading" + level, text);

    public DocxBuilder Paragraph(string text) => Runs((text, false, false));

    // Runs of (text, bold, italic) in one paragraph.
    public DocxBuilder Runs(params (string Text, bool Bold, bool Italic)[] runs)
    {
        var paragraph = new Paragraph();
        foreach (var (text, bold, italic) in runs)
        {
            var run = paragraph.AppendChild(new Run());
            var properties = run.AppendChild(new RunProperties());
            if (bold)
            {
                properties.AppendChild(new Bold());
            }
            if (italic)
            {
                properties.AppendChild(new Italic());
            }
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }
        _blocks.Add(paragraph);
        return this;
    }

    // The end of a section, as Word writes it: an empty paragraph holding the section properties.
    public DocxBuilder SectionBreak()
    {
        var paragraph = new Paragraph();
        var section = paragraph.AppendChild(new ParagraphProperties()).AppendChild(new SectionProperties());
        section.AppendChild(new SectionType { Val = SectionMarkValues.NextPage });
        _blocks.Add(paragraph);
        return this;
    }

    public DocxBuilder Image()
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run()).AppendChild(new Drawing());
        _blocks.Add(paragraph);
        return this;
    }

    public DocxBuilder Comment(string text)
    {
        _comment = text;
        return this;
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());
            foreach (var block in _blocks)
            {
                body.AppendChild(block.CloneNode(true));
            }
            var styles = main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles();
            foreach (var (id, name) in new[] { ("Normal", "Normal"), ("Title", "Title"), ("Subtitle", "Subtitle"), ("Heading1", "heading 1"), ("Heading2", "heading 2") })
            {
                var style = styles.AppendChild(new Style { Type = StyleValues.Paragraph, StyleId = id });
                style.AppendChild(new StyleName { Val = name });
            }
            if (_comment != null)
            {
                var comments = main.AddNewPart<WordprocessingCommentsPart>().Comments = new Comments();
                var comment = comments.AppendChild(new Comment { Id = "0", Author = "Reviewer" });
                comment.AppendChild(new Paragraph()).AppendChild(new Run()).AppendChild(new Text(_comment));
            }
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private DocxBuilder Styled(string styleId, string text)
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new ParagraphProperties()).AppendChild(new ParagraphStyleId { Val = styleId });
        paragraph.AppendChild(new Run()).AppendChild(new Text(text));
        _blocks.Add(paragraph);
        return this;
    }

    // A package whose main part nests `depth` structured-document tags: a few KB zipped, and what the Open XML SDK cannot
    // load without overflowing the stack. Hand-written XML, since the SDK is what it must never be asked to read.
    public static byte[] DeeplyNested(int depth) => PackageOf(
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>"
        + string.Concat(Enumerable.Repeat("<w:sdt><w:sdtContent>", depth))
        + "<w:r><w:t>deep</w:t></w:r>"
        + string.Concat(Enumerable.Repeat("</w:sdtContent></w:sdt>", depth))
        + "</w:p></w:body></w:document>");

    // A package with the given main part and the relationship parts that name it.
    public static byte[] PackageOf(string documentXml, string mainPartName = "word/document.xml") => TestZip.Of(
        ("[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/" + mainPartName
            + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"),
        ("_rels/.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"/" + mainPartName + "\"/></Relationships>"),
        (mainPartName, documentXml));
}
