using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print.Templates;

namespace Codex.WebApi.Tests.Print;

// Template packages for upload tests: unpack a downloaded zip, change files, zip it again, and post it as a form.
public static class TemplateZips
{
    // Unpacks the zip into a new temp folder (deleted by the caller through the returned path's parent).
    public static string Unpack(byte[] zip)
    {
        var folder = Path.Combine(Path.GetTempPath(), "codex-template-zips", Guid.NewGuid().ToString("N"), "package");
        ZipFile.ExtractToDirectory(new MemoryStream(zip), folder);
        return folder;
    }

    // Edits template.json in place.
    public static void EditManifest(string folder, Action<JsonObject> edit)
    {
        var path = Path.Combine(folder, TemplatePackage.ManifestFile);
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(manifest);
        File.WriteAllText(path, manifest.ToJsonString());
    }

    public static byte[] Zip(string folder) => TemplateArchive.Zip(folder);

    public static MultipartFormDataContent Form(byte[] zip, string fileName = "template.zip")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", fileName);
        return form;
    }

    public static void Delete(string folder)
    {
        var root = Path.GetDirectoryName(folder)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
