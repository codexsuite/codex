using Codex.Contracts;
using Codex.Contracts.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.Application.Storage;

// CODEX_BLOB_ROOT is the blob folder; a relative path resolves against the host's content root.
// It is read when the store is first resolved, so test hosts can set it after registration.
public sealed class StorageServices : IServiceModule
{
    public const string BlobRootKey = "CODEX_BLOB_ROOT";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
        => services.AddSingleton<IBlobStore>(sp =>
        {
            var root = sp.GetRequiredService<IConfiguration>()[BlobRootKey]
                       ?? throw new InvalidOperationException($"{BlobRootKey} is not set.");
            var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
            return new LocalDiskBlobStore(Path.GetFullPath(root, contentRoot));
        });
}
