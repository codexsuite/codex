using System.IO.Compression;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// What the archive importers (Novelcrafter .zip, .docx) share: the one-file upload, opening it as a zip, and the guards
// against a hostile one. An archive is never extracted to disk, so zip-slip is a path check, not a write; entry names
// that are not plain relative paths refuse the whole upload, as ImportLimits.CheckPath does for a folder.
internal static class ImportArchive
{
    private const int MaxNameLength = 400;

    // The single file of a single-file source, checked for its extension.
    public static ImportFile Single(ImportUpload upload, string extension, string what)
    {
        if (upload.Files.Count != 1)
        {
            throw ApiException.Validation($"Send one {what} file.");
        }
        var file = upload.Files[0];
        if (!file.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation($"\"{file.Path}\" is not a {what} ({extension}).");
        }
        return file;
    }

    public static ZipArchive Open(byte[] content, string what)
    {
        try
        {
            return new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw Corrupt(what);
        }
    }

    // The files in the archive (not folders, not macOS resource forks), each with a checked name. At most
    // ImportLimits.MaxFiles entries, names unique.
    public static IReadOnlyList<ZipArchiveEntry> Files(ZipArchive archive, string what)
    {
        try
        {
            if (archive.Entries.Count > ImportLimits.MaxFiles)
            {
                throw ApiException.Validation($"An import can hold at most {ImportLimits.MaxFiles} files; this archive has {archive.Entries.Count}.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<ZipArchiveEntry>();
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.TrimEnd('/');
                CheckName(name, entry.FullName);
                if (entry.FullName.EndsWith('/') || name.StartsWith("__MACOSX/", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!names.Add(name))
                {
                    throw ApiException.Validation($"The {what} lists \"{name}\" twice; nothing was imported.");
                }
                files.Add(entry);
            }
            return files;
        }
        catch (InvalidDataException)
        {
            throw Corrupt(what);
        }
    }

    // The entries about to be read must not expand past the import's size cap, nor past MaxCompressionRatio times the
    // size they are stored at.
    public static void CheckExpansion(IReadOnlyCollection<ZipArchiveEntry> entries, string what)
    {
        var declared = entries.Sum(e => e.Length);
        if (declared > ImportLimits.MaxTotalBytes)
        {
            throw ApiException.Validation($"The {what} expands to more than {ImportLimits.MaxTotalBytes / (1024 * 1024)} MB, which is more than an import can hold.");
        }
        var stored = Math.Max(1, entries.Sum(e => e.CompressedLength));
        if (declared > ImportLimits.RatioCheckFloorBytes && declared / stored > ImportLimits.MaxCompressionRatio)
        {
            throw ApiException.Validation($"The {what} expands far beyond its size ({declared / stored} to 1), which no real export does; nothing was imported.");
        }
    }

    // The entry's bytes. The size in the archive's index is only a claim: the read stops at the cap, and an entry whose
    // data is not exactly the size it declares is refused (a reader may otherwise cut it short without saying so).
    public static byte[] Read(ZipArchiveEntry entry, long limit, string what)
    {
        try
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    throw ApiException.Validation($"\"{entry.FullName}\" in the {what} is larger than the archive says; the file is damaged.");
                }
                buffer.Write(chunk, 0, read);
            }
            // Fewer or more bytes than declared, or bytes that do not match the checksum the archive stores: a reader may cut a
            // short entry's data off without saying so, and the checksum is what shows it.
            if (buffer.Length != entry.Length || Crc32(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) != entry.Crc32)
            {
                throw ApiException.Validation($"\"{entry.FullName}\" in the {what} is not the size the archive says; the file is damaged.");
            }
            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            throw Corrupt(what);
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    // Relative, forward slashes, no empty, "." or ".." segment, no drive letter or control character. (Not CheckPath: a
    // colon is fine inside a file name in an archive.)
    private static void CheckName(string name, string original)
    {
        var segments = name.Split('/');
        var safe = name.Length is > 0 and <= MaxNameLength
            && !name.StartsWith('/') && !name.Contains('\\') && !name.Any(char.IsControl)
            && !(name.Length > 1 && name[1] == ':' && char.IsAsciiLetter(name[0]))
            && !segments.Any(segment => segment is "" or "." or "..");
        if (!safe)
        {
            throw ApiException.Validation($"The archive has an entry named \"{original}\", which is not a path inside it; nothing was imported.");
        }
    }

    private static ApiException Corrupt(string what) =>
        ApiException.Validation($"This is not a readable {what}: the zip archive is damaged or is not a zip file.");
}
