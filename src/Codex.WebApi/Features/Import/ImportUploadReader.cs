using Codex.Application.Features.Import;
using Codex.Contracts.Errors;

namespace Codex.WebApi.Features.Import;

// Reads an import's multipart body: the source, the folder's files and the paths the client left out. Kept out of
// ImportsController, which stays a one-line delegation per route.
public static class ImportUploadReader
{
    // Form fields naming files the client did not send (repeatable): not Markdown, or over ImportLimits.MaxFileBytes.
    public const string NotMarkdownField = "notMarkdown";
    public const string TooLargeField = "tooLarge";

    public const int MaxFormValues = (ImportLimits.MaxFiles * 2) + 16;

    public static async Task<ImportUpload> ReadAsync(HttpRequest http, CancellationToken ct)
    {
        if (!http.HasFormContentType)
        {
            throw ApiException.Validation("Send the import as multipart form data: a source and the folder's files.");
        }
        IFormCollection form;
        try
        {
            form = await http.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            // The form reader's own caps (part count, part size) throw before the endpoint can count anything.
            throw ApiException.Validation($"An import can hold at most {ImportLimits.MaxFiles} files; pick a smaller folder.");
        }
        var source = form["source"].ToString();
        if (source.Length == 0)
        {
            throw ApiException.Validation("Say which source the files come from (source).");
        }
        // Files the browser left out on purpose: listed as skipped, never sent.
        var omitted = form[NotMarkdownField].Select(p => new ImportSkippedItem(ImportLimits.CheckPath(p), ImportLimits.NotMarkdown))
            .Concat(form[TooLargeField].Select(p => new ImportSkippedItem(ImportLimits.CheckPath(p), ImportLimits.TooLarge)))
            .ToList();
        var takesImages = ImportLimits.TakesImages(source);
        bool IsImage(IFormFile file) => takesImages && PandocFolderParser.IsImage(file.FileName);
        ImportLimits.CheckTotals(form.Files.Count + omitted.Count, form.Files.Where(f => !IsImage(f)).Sum(f => f.Length));
        ImportLimits.CheckImageTotal(form.Files.Where(IsImage).Sum(f => f.Length));
        var files = new List<ImportFile>(form.Files.Count);
        var images = new List<ImportImage>();
        var seen = new HashSet<string>(omitted.Select(o => o.Path), StringComparer.Ordinal);
        foreach (var file in form.Files)
        {
            var path = ImportLimits.CheckPath(file.FileName);
            if (!seen.Add(path))
            {
                throw ApiException.Validation($"\"{path}\" is in the upload twice.");
            }
            if (IsImage(file))
            {
                if (file.Length > ImportLimits.MaxImageBytes)
                {
                    omitted.Add(new ImportSkippedItem(path, ImportLimits.TooLarge));
                }
                else
                {
                    images.Add(new ImportImage(path, file.Length, file.OpenReadStream));
                }
                continue;
            }
            if (file.Length > ImportLimits.MaxUploadedFileBytes(source))
            {
                omitted.Add(new ImportSkippedItem(path, ImportLimits.TooLarge));
                continue;
            }
            using var buffer = new MemoryStream((int)file.Length);
            await using (var stream = file.OpenReadStream())
            {
                await stream.CopyToAsync(buffer, ct);
            }
            files.Add(new ImportFile(path, buffer.ToArray()));
        }
        return new ImportUpload(source, files, omitted, images);
    }
}
