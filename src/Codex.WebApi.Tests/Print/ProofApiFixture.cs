using System.Diagnostics;
using System.Net.Http.Json;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Shared by the proof tests: waiting for the in-process proof worker by polling the API.
public abstract class ProofApiFixture : ApiFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    // Polls the proof until the worker has settled it.
    protected async Task<ProofDto> SettledAsync(Guid proofId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var proof = (await Client.GetFromJsonAsync<ProofDto>($"/api/proofs/{proofId}"))!;
            if (proof.Status is "succeeded" or "failed")
            {
                return proof;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the proof worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}
