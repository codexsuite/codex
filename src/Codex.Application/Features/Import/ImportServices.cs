using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Import;

// One IImportParser per book_import source; ImportService picks by Source.
public sealed class ImportServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IImportParser, MarkdownFolderParser>();
        services.AddSingleton<IImportParser, NovelcrafterZipParser>();
        services.AddSingleton<IImportParser, DocxParser>();
        services.AddSingleton<IImportParser, PandocFolderParser>();
        services.AddSingleton<IImportParser, CodexBackupParser>();
    }
}
