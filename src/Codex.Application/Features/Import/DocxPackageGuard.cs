using System.IO.Compression;
using System.Xml;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// Checks a .docx before the Open XML SDK loads it. The SDK builds each part as a tree by recursion, so a part nested a
// few thousand elements deep overflows the stack, which no handler can catch and which ends the process. Every XML part
// (the SDK loads them all: document, styles, comments, notes, relationships, properties) is therefore streamed through
// a plain XmlReader first: no DTDs, no external resolver, a size cap per part and a depth cap. The main part is the one
// _rels/.rels names, not assumed to be word/document.xml.
internal static class DocxPackageGuard
{
    // Characters of the main document part (a 100,000-word novel is a few MB of XML) and of every other part.
    public const int MaxMainPartChars = 16 * 1024 * 1024;
    public const int MaxOtherPartChars = 4 * 1024 * 1024;

    // Deeper than any real document (Word nests a paragraph perhaps ten levels), far below the stack's limit.
    public const int MaxDepth = 256;

    private const string PackageRels = "_rels/.rels";
    private const string OfficeDocument = "/officeDocument";

    public static void Check(IReadOnlyList<ZipArchiveEntry> entries, string what)
    {
        var rels = entries.FirstOrDefault(e => e.FullName.Equals(PackageRels, StringComparison.OrdinalIgnoreCase))
            ?? throw NotWord();
        var main = MainPartName(rels, what) ?? throw NotWord();
        if (!entries.Any(e => e.FullName.Equals(main, StringComparison.OrdinalIgnoreCase)))
        {
            throw NotWord();
        }
        foreach (var entry in entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                                 || e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var limit = entry.FullName.Equals(main, StringComparison.OrdinalIgnoreCase) ? MaxMainPartChars : MaxOtherPartChars;
            if (entry.Length > limit)
            {
                throw ApiException.Validation(
                    $"\"{entry.FullName}\" in the {what} is {entry.Length / (1024 * 1024)} MB, more than an import can read ({limit / (1024 * 1024)} MB for this part).");
            }
            Scan(entry, limit, what, null);
        }
    }

    // The target of the package's officeDocument relationship, as an archive name.
    private static string? MainPartName(ZipArchiveEntry rels, string what)
    {
        string? target = null;
        Scan(rels, MaxOtherPartChars, what, reader =>
        {
            if (target == null && reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                && reader.GetAttribute("Type") is { } type && type.EndsWith(OfficeDocument, StringComparison.Ordinal))
            {
                target = reader.GetAttribute("Target");
            }
        });
        return target?.TrimStart('/');
    }

    private static void Scan(ZipArchiveEntry entry, int limit, string what, Action<XmlReader>? onNode)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limit };
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.Depth > MaxDepth)
                {
                    throw ApiException.Validation($"\"{entry.FullName}\" in the {what} is nested more than {MaxDepth} levels deep, which no real document is.");
                }
                onNode?.Invoke(reader);
            }
        }
        catch (Exception e) when (e is XmlException or InvalidDataException)
        {
            throw ApiException.Validation($"\"{entry.FullName}\" in the {what} is not readable XML (or is larger than an import can read).");
        }
    }

    private static ApiException NotWord() =>
        ApiException.Validation("This zip is not a Word document: its package names no main document part.");
}
