namespace Codex.Application.Features.Import;

// Step one of the import pipeline (parse -> preview -> write): turns an upload into a format-neutral ImportPlan.
// One implementation per book_import source, registered in DI; ImportService picks it by Source. A parser never
// touches the database. It throws ApiException.Validation when the upload is not that format at all, and lists
// anything it leaves out in ImportPlan.Skipped.
public interface IImportParser
{
    string Source { get; }

    ImportPlan Parse(ImportUpload upload);
}
