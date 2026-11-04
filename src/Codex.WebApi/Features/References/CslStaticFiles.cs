using Codex.Contracts;
using Microsoft.AspNetCore.StaticFiles;

namespace Codex.WebApi.Features.References;

// The bibliography loads its styles from /csl/*.csl (src/Codex.Client/public/csl). Static file serving skips
// extensions it has no content type for, so the SPA fallback would answer with index.html: name .csl here.
public sealed class CslStaticFiles : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StaticFileOptions>(options =>
        {
            var types = new FileExtensionContentTypeProvider();
            types.Mappings[".csl"] = "application/vnd.citationstyles.style+xml";
            options.ContentTypeProvider = types;
        });
    }
}
