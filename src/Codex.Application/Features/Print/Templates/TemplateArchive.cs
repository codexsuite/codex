using System.IO.Compression;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Print.Templates;

// A template package as one .zip: how a version is stored (blob store), downloaded and uploaded. Zip writes a folder
// with its files in path order and fixed timestamps, so the same files always give the same bytes. Extract unpacks an
// untrusted upload or stored package into an empty folder and refuses (400 validation) anything that could reach
// outside it or exhaust the disk: absolute or climbing paths (zip slip), backslashes, drive letters and control
// characters in names, symbolic links and other non-regular entries, duplicate paths, more than MaxFiles files, more
// than MaxFileBytes in one file or MaxTotalBytes in all (counted while inflating, not taken from the headers). A zip
// whose files all sit in one top folder that holds template.json (what zipping the package folder gives) is unpacked
// from inside that folder.
public static class TemplateArchive
{
    public const long MaxZipBytes = 25L * 1024 * 1024;
    public const long MaxTotalBytes = 64L * 1024 * 1024;
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const int MaxFiles = 1000;

    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Unix file type bits of ZipArchiveEntry.ExternalAttributes >> 16.
    private const int TypeMask = 0xF000;
    private const int RegularFile = 0x8000;
    private const int Directory = 0x4000;

    public static byte[] Zip(string directory)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/'))
                         .Order(StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTime;
                entry.ExternalAttributes = (RegularFile | 0x1A4) << 16; // -rw-r--r--
                using var target = entry.Open();
                using var source = File.OpenRead(Path.Combine(directory, path));
                source.CopyTo(target);
            }
        }

        return buffer.ToArray();
    }

    // Unpacks the zip into target (created; must not exist yet or be empty). 400 when the zip is not one, or breaks a rule above.
    public static void Extract(Stream zip, string target)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw ApiException.Validation("The template package is not a .zip file.");
        }

        using (archive)
        {
            var files = Plan(archive);
            var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            System.IO.Directory.CreateDirectory(target);
            long total = 0;
            foreach (var (entry, relative) in files)
            {
                var path = Path.GetFullPath(Path.Combine(target, relative));
                if (!path.StartsWith(root, StringComparison.Ordinal))
                {
                    throw Bad($"The entry {entry.FullName} points outside the package.");
                }

                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var source = OpenEntry(entry);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                total += CopyCapped(source, file, entry.FullName, MaxTotalBytes - total);
            }
        }
    }

    // The regular files to write and their paths inside the package, after every name and count check.
    private static List<(ZipArchiveEntry Entry, string Path)> Plan(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxFiles * 2)
        {
            throw Bad($"The package has more than {MaxFiles} files.");
        }

        var files = new List<ZipArchiveEntry>();
        foreach (var entry in archive.Entries)
        {
            CheckName(entry.FullName);
            var type = (entry.ExternalAttributes >> 16) & TypeMask;
            var isFolder = entry.FullName.EndsWith('/');
            if (isFolder && type is 0 or Directory)
            {
                continue;
            }
            if (isFolder || type is not (0 or RegularFile))
            {
                throw Bad($"The entry {entry.FullName} is a link or special file; a package holds regular files only.");
            }
            if (entry.Length > MaxFileBytes)
            {
                throw Bad($"The file {entry.FullName} is larger than {MaxFileBytes / (1024 * 1024)} MB.");
            }
            files.Add(entry);
        }

        if (files.Count > MaxFiles)
        {
            throw Bad($"The package has more than {MaxFiles} files.");
        }

        var prefix = TopFolder(files);
        var planned = files.Select(e => (Entry: e, Path: e.FullName[prefix.Length..])).ToList();
        var duplicate = planned.GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw Bad($"The package lists {duplicate.Key} more than once.");
        }

        return planned;
    }

    private static void CheckName(string name)
    {
        var segments = name.TrimEnd('/').Split('/');
        if (name.Length == 0 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':')
            || name.Any(char.IsControl) || segments.Any(s => s is "" or "." or ".."))
        {
            throw Bad($"The entry name '{name}' is not a plain relative path.");
        }
    }

    // "pkg/" when every file is under one folder and template.json is not at the top but in that folder; else "".
    private static string TopFolder(List<ZipArchiveEntry> files)
    {
        if (files.Any(f => f.FullName == TemplatePackage.ManifestFile))
        {
            return string.Empty;
        }

        var tops = files.Select(f => f.FullName.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();
        return tops.Count == 1 && files.Any(f => f.FullName == tops[0] + "/" + TemplatePackage.ManifestFile) ? tops[0] + "/" : string.Empty;
    }

    private static Stream OpenEntry(ZipArchiveEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (InvalidDataException)
        {
            throw Bad($"The entry {entry.FullName} cannot be read (unsupported compression or a damaged zip).");
        }
    }

    private static long CopyCapped(Stream source, Stream target, string name, long remaining)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        try
        {
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > MaxFileBytes)
                {
                    throw Bad($"The file {name} is larger than {MaxFileBytes / (1024 * 1024)} MB.");
                }
                if (written > remaining)
                {
                    throw Bad($"The package unpacks to more than {MaxTotalBytes / (1024 * 1024)} MB.");
                }
                target.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException)
        {
            throw Bad($"The entry {name} cannot be read (a damaged zip).");
        }

        return written;
    }

    private static ApiException Bad(string problem) => ApiException.Validation($"The template package was refused: {problem}");
}
