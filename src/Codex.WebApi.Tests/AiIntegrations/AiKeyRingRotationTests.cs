using System.Net.Http.Json;
using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai.Integrations;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.AiIntegrations;

// A key saved under the first key in the ring still decrypts after the ring rotates (a new key becomes the default
// for new credentials; nothing is revoked).
[TestFixture]
public class AiKeyRingRotationTests : AiIntegrationFixture
{
    private const string Key = "sk-rotate-ROTATED0123456789";

    [Test]
    public async Task Credential_WhenTheKeyRingRotates_ShouldStillDecrypt()
    {
        using var create = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(Key));
        create.EnsureSuccessStatusCode();
        await using var db = await OpenDatabaseAsync();
        var oldCredential = await db.QuerySingleAsync<string>("SELECT credential FROM ai_integration");
        var ring = new DirectoryInfo(Path.Combine(Host.BlobRoot, "data-protection"));
        var keysBefore = ring.GetFiles("key-*.xml").Length;

        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(AiIntegrationServices.ApplicationName).PersistKeysToFileSystem(ring);
        var rotated = new DefaultServiceProviderFactory().CreateServiceProvider(services);
        var now = TimeProvider.System.GetUtcNow();
        rotated.GetRequiredService<IKeyManager>().CreateNewKey(now.AddSeconds(-1), now.AddDays(90));
        var afterRotation = new AiCredentialProtector(rotated.GetRequiredService<IDataProtectionProvider>());
        var newCredential = afterRotation.Protect(Key);

        keysBefore.ShouldBe(1);
        ring.GetFiles("key-*.xml").Length.ShouldBe(2);
        afterRotation.Unprotect(oldCredential).ShouldBe(Key);
        afterRotation.Unprotect(newCredential).ShouldBe(Key);
        Host.Services.GetRequiredService<IAiCredentialProtector>().Unprotect(oldCredential).ShouldBe(Key);
    }
}
