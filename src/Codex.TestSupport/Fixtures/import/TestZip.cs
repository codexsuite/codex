using System.IO.Compression;

namespace Codex.TestSupport.Fixtures;

// Zips built in test code, so no binary is committed: a folder of text fixtures, or named entries (hostile names and
// all, which ZipFile.CreateFromDirectory could not make).
public static class TestZip
{
    public static byte[] FromFolder(string folder, string rootName)
    {
        var entries = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => (rootName + "/" + Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes(f)));
        return Of(entries);
    }

    public static byte[] Of(IEnumerable<(string Name, byte[] Content)> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
        }
        return stream.ToArray();
    }

    public static byte[] Of(params (string Name, string Text)[] entries) =>
        Of(entries.Select(e => (e.Name, System.Text.Encoding.UTF8.GetBytes(e.Text))));

    // The same archive with every entry's uncompressed size, in its local and central headers, set to `size`: an index that
    // lies about the data (the entries must be written with sizes in their local headers, as Of does).
    public static byte[] WithDeclaredSize(byte[] zip, uint size)
    {
        var patched = (byte[])zip.Clone();
        for (var i = 0; i + 30 < patched.Length; i++)
        {
            if (patched[i] == 0x50 && patched[i + 1] == 0x4b && patched[i + 2] == 0x03 && patched[i + 3] == 0x04)
            {
                BitConverter.TryWriteBytes(patched.AsSpan(i + 22, 4), size);
            }
            else if (patched[i] == 0x50 && patched[i + 1] == 0x4b && patched[i + 2] == 0x01 && patched[i + 3] == 0x02)
            {
                BitConverter.TryWriteBytes(patched.AsSpan(i + 24, 4), size);
            }
        }
        return patched;
    }
}
