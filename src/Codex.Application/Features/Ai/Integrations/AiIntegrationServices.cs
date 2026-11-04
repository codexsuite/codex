using Codex.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.Application.Features.Ai.Integrations;

// The credential protector's own Data Protection provider, keyed from CODEX_DATA_PROTECTION_DIR (not the app's
// Postgres-backed ring that signs cookies), read when first resolved so a test override is honoured. Without the
// setting the server still starts; saving or using a key then fails with not_configured naming the setting.
public sealed class AiIntegrationServices : IServiceModule
{
    public const string ApplicationName = "codex-ai-integrations";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAiCredentialProtector>(sp =>
        {
            var dir = sp.GetRequiredService<IConfiguration>()[AiSettings.DataProtectionDirKey];
            if (string.IsNullOrWhiteSpace(dir))
            {
                return new MissingKeyRingProtector();
            }

            var root = Path.GetFullPath(dir, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
            Directory.CreateDirectory(root);
            return new AiCredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(root), b => b.SetApplicationName(ApplicationName)));
        });
    }
}
