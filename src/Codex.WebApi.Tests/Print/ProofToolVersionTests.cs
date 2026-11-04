using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// A proof's cache key covers the pandoc and Tectonic versions. The web and worker images install different tools, so
// both set CODEX_TOOLCHAIN at build time and hash the same string (ToolVersions). The test host runs web and worker in
// one process, so a switchable ToolVersions stands in for the build-time value and lets a test "upgrade" the toolchain.
// Needs the print toolchain on PATH, as CI installs it.
[TestFixture]
public class ProofToolVersionTests : ProofApiFixture
{
    private static readonly string BookBase = $"/api/books/{SeedIds.Books.PlayersGuide}";

    private static readonly Guid Chapter = SeedIds.Chapters.PlayersGuideCh1;

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
        services.AddSingleton<ToolVersions>(sp => new SwitchableTools(sp.GetRequiredService<RenderSettings>()));

    [Test]
    public async Task Proof_WhenTheWorkerFinishesWithNoEdit_ShouldReadAsCurrentInTheWebProcessAndBeServedFromTheCache()
    {
        var (queued, proof) = await RequestAsync();

        proof.CacheKey.ShouldBe(queued.CacheKey);
        var list = await ListAsync();
        list.CurrentCacheKey.ShouldBe(queued.CacheKey);
        list.Proofs.Single(p => p.Id == proof.Id).CacheKey.ShouldBe(list.CurrentCacheKey);

        using var again = await Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(Chapter));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cached = (await again.Content.ReadFromJsonAsync<ProofDto>()).ShouldNotBeNull();
        (cached.Cached, cached.Id).ShouldBe((true, proof.Id));
    }

    [Test]
    public async Task Proof_WhenTheToolchainChangesAfterTheProofWasMade_ShouldNotReadAsCurrentOrComeFromTheCache()
    {
        var (queued, proof) = await RequestAsync();

        SwitchableTools.Current = ToolchainWith("0.16.0");

        var list = await ListAsync();
        list.CurrentCacheKey.ShouldNotBe(proof.CacheKey);
        list.CurrentCacheKey.ShouldNotBe(queued.CacheKey);
        using var again = await Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(Chapter));
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var fresh = (await again.Content.ReadFromJsonAsync<ProofDto>()).ShouldNotBeNull();
        fresh.Cached.ShouldBeFalse();
        fresh.Id.ShouldNotBe(proof.Id);
        var rebuilt = await SettledAsync(fresh.Id);
        rebuilt.CacheKey.ShouldBe(fresh.CacheKey);
        (await ListAsync()).CurrentCacheKey.ShouldBe(rebuilt.CacheKey);
    }

    // The two images pin pandoc and Tectonic separately, and CODEX_TOOLCHAIN is built from those pins, so they must agree.
    [Test]
    public void Dockerfiles_ShouldPinTheSameToolVersionsAndBuildTheSameToolchainString()
    {
        var src = FindSrc();
        var web = File.ReadAllText(Path.Combine(src, "Dockerfile"));
        var worker = File.ReadAllText(Path.Combine(src, "Dockerfile.worker"));

        foreach (var pin in new[] { "PANDOC_VERSION", "TECTONIC_VERSION" })
        {
            Pin(web, pin).ShouldBe(Pin(worker, pin), pin);
        }

        Toolchain(web).ShouldBe(Toolchain(worker));
        Toolchain(web).ShouldNotBeNull();
    }

    // The tests of this class share one database, so each starts on a toolchain of its own: its first request is a miss.
    private static string ToolchainWith(string tectonic) => $"pandoc 3.12|Tectonic {tectonic}";

    private async Task<(ProofDto Queued, ProofDto Proof)> RequestAsync()
    {
        SwitchableTools.Current = ToolchainWith($"0.15.0+{Guid.NewGuid():N}");
        using var response = await Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(Chapter));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await response.Content.ReadFromJsonAsync<ProofDto>()).ShouldNotBeNull();
        var proof = await SettledAsync(queued.Id);
        proof.Status.ShouldBe("succeeded", proof.Log);
        return (queued, proof);
    }

    private async Task<ProofListDto> ListAsync() =>
        (await Client.GetFromJsonAsync<ProofListDto>($"{BookBase}/proofs?chapterId={Chapter}")).ShouldNotBeNull();

    private static string FindSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Dockerfile.worker")))
            {
                return Path.Combine(dir.FullName, "src");
            }
        }

        throw new FileNotFoundException("src/Dockerfile.worker not found above the test output");
    }

    private static string? Pin(string dockerfile, string name) =>
        Regex.Match(dockerfile, $@"^ARG {name}=(\S+)", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? Toolchain(string dockerfile) =>
        Regex.Match(dockerfile, @"^ENV CODEX_TOOLCHAIN=""([^""]+)""", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

    private sealed class SwitchableTools(RenderSettings settings) : ToolVersions(settings)
    {
        public static string Current { get; set; } = "pandoc 3.12|Tectonic 0.15.0";

        public override string Value => Current;
    }
}
