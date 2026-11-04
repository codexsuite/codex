using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.DataProtection;

namespace Codex.UnitTest.Ai;

// AiCredentialProtector over real file-system key rings: a key round-trips, a ring that never held the key (the key
// folder was lost or replaced) is not_configured rather than a crash, and the hint shows nothing more than it should.
[TestFixture]
public class AiCredentialProtectorTests
{
    private const string Key = "sk-proj-0123456789abcdefWXYZ";

    private readonly List<string> _folders = [];

    [TearDown]
    public void RemoveFolders()
    {
        foreach (var folder in _folders)
        {
            Directory.Delete(folder, recursive: true);
        }
        _folders.Clear();
    }

    [Test]
    public void Unprotect_WhenTheSameRingProtectedIt_ShouldReturnTheKey()
    {
        var protector = Protector(NewFolder());

        var credential = protector.Protect(Key);

        credential.ShouldNotContain(Key);
        protector.Unprotect(credential).ShouldBe(Key);
    }

    [Test]
    public void Unprotect_WhenTheKeyFolderWasReplaced_ShouldReturnNotConfiguredWithoutTheKey()
    {
        var credential = Protector(NewFolder()).Protect(Key);

        var failure = Should.Throw<ApiException>(() => Protector(NewFolder()).Unprotect(credential));

        failure.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        failure.Message.ShouldBe("The saved key can't be read with this server's key folder. Enter the key again in Settings → AI.");
    }

    [TestCase("sk-proj-0123456789abcdefWXYZ", "sk-…WXYZ")]
    [TestCase("sk-or-v1-0123456789abcdef9876", "sk-…9876")]
    [TestCase("0123456789abcdefLAST", "…LAST")]
    [TestCase("  sk-0123456789ab  ", "…89ab")]
    [TestCase("sk-1234567890abcdef", "…cdef")]
    [TestCase("short-key", "…-key")]
    [TestCase("sk-1234", "…")]
    public void Hint_WhenGivenAKey_ShouldShowOnlyWhatALongKeyCanSpare(string key, string expected)
    {
        AiCredentialProtector.Hint(key).ShouldBe(expected);
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "codex-ai-ring-tests", Guid.NewGuid().ToString("N"));
        _folders.Add(folder);
        return folder;
    }

    private static AiCredentialProtector Protector(string folder) => new(
        DataProtectionProvider.Create(new DirectoryInfo(folder), b => b.SetApplicationName(AiIntegrationServices.ApplicationName)));
}
