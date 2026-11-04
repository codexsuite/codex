using System.IO.Compression;
using System.Text;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Errors;

namespace Codex.UnitTest.Application;

// Template packages as zips: the stored form is deterministic, and an untrusted upload never writes outside its folder,
// never plants a link and never unpacks past the caps.
[TestFixture]
public class TemplateArchiveTests
{
    private readonly List<string> _folders = [];

    [TearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
        _folders.Clear();
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "codex-archive-tests", Guid.NewGuid().ToString("N"));
        _folders.Add(folder);
        return folder;
    }

    // A zip of (name, text, unix mode) entries; mode null leaves the attributes unset.
    private static MemoryStream ZipOf(params (string Name, string Text, int? Mode)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text, mode) in entries)
            {
                var entry = zip.CreateEntry(name);
                if (mode is { } m)
                {
                    entry.ExternalAttributes = m << 16;
                }
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    private static string Refused(Stream zip, string target)
    {
        var error = Should.Throw<ApiException>(() => TemplateArchive.Extract(zip, target));
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        return error.Message;
    }

    [Test]
    public void Zip_WhenTheSameFolderIsZippedTwice_ShouldGiveTheSameBytesAndUnpackToTheSameFiles()
    {
        var source = NewFolder();
        Directory.CreateDirectory(Path.Combine(source, "filters"));
        File.WriteAllText(Path.Combine(source, "template.json"), "{}");
        File.WriteAllText(Path.Combine(source, "filters", "codex.lua"), "return {}");

        var first = TemplateArchive.Zip(source);
        var second = TemplateArchive.Zip(source);
        var target = Path.Combine(NewFolder(), "pkg");
        TemplateArchive.Extract(new MemoryStream(first), target);

        second.ShouldBe(first);
        File.ReadAllText(Path.Combine(target, "filters", "codex.lua")).ShouldBe("return {}");
        File.ReadAllText(Path.Combine(target, "template.json")).ShouldBe("{}");
    }

    [Test]
    public void Extract_WhenEveryFileSitsInOneTopFolder_ShouldUnpackFromInsideIt()
    {
        var target = Path.Combine(NewFolder(), "pkg");

        TemplateArchive.Extract(ZipOf(("my-fork/template.json", "{}", null), ("my-fork/wrapper.tex", "$body$", null)), target);

        File.Exists(Path.Combine(target, "template.json")).ShouldBeTrue();
        File.Exists(Path.Combine(target, "wrapper.tex")).ShouldBeTrue();
    }

    [TestCase("../escape.tex")]
    [TestCase("tex/../../escape.tex")]
    [TestCase("/etc/escape.tex")]
    [TestCase("tex\\..\\..\\escape.tex")]
    [TestCase("C:/escape.tex")]
    [TestCase("tex/./x.tex")]
    public void Extract_WhenAnEntryNameClimbsOutOrIsRooted_ShouldRefuseAndWriteNothingOutside(string name)
    {
        var root = NewFolder();
        var target = Path.Combine(root, "pkg");

        Refused(ZipOf(("template.json", "{}", null), (name, "x", null)), target).ShouldContain("not a plain relative path");

        File.Exists(Path.Combine(root, "escape.tex")).ShouldBeFalse();
    }

    [Test]
    public void Extract_WhenAnEntryIsASymbolicLink_ShouldRefuseIt()
    {
        Refused(ZipOf(("template.json", "{}", null), ("tex/passwd", "/etc/passwd", 0xA1FF)), Path.Combine(NewFolder(), "pkg"))
            .ShouldContain("link or special file");
    }

    [Test]
    public void Extract_WhenTwoEntriesHaveTheSamePath_ShouldRefuseThem()
    {
        Refused(ZipOf(("template.json", "{}", null), ("wrapper.tex", "a", null), ("Wrapper.tex", "b", null)), Path.Combine(NewFolder(), "pkg"))
            .ShouldContain("more than once");
    }

    [Test]
    public void Extract_WhenThereAreTooManyFiles_ShouldRefuseThem()
    {
        var entries = Enumerable.Range(0, TemplateArchive.MaxFiles + 1).Select(i => ($"tex/f{i}.sty", "%", (int?)null)).ToArray();

        Refused(ZipOf(entries), Path.Combine(NewFolder(), "pkg")).ShouldContain($"more than {TemplateArchive.MaxFiles} files");
    }

    [Test]
    public void Extract_WhenAFileInflatesPastTheCap_ShouldStopAndRefuse()
    {
        var bomb = new MemoryStream();
        using (var zip = new ZipArchive(bomb, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("tex/big.sty", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i <= TemplateArchive.MaxFileBytes / zeros.Length; i++)
            {
                stream.Write(zeros);
            }
        }
        bomb.Position = 0;

        Refused(bomb, Path.Combine(NewFolder(), "pkg")).ShouldContain("larger than");
    }

    [Test]
    public void Extract_WhenTheUploadIsNotAZip_ShouldSaySo()
    {
        Refused(new MemoryStream("not a zip"u8.ToArray()), Path.Combine(NewFolder(), "pkg")).ShouldContain("not a .zip");
    }
}
