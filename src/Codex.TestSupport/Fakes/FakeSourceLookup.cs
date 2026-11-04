using System.Text.Json;
using Codex.Contracts.Sources;

namespace Codex.TestSupport.Fakes;

// Answers from Fixtures/source-lookup.json: [{ kind, value, result: SourceLookupResult }]. Anything not in
// the file is not_found.
public sealed class FakeSourceLookup : ISourceLookup
{
    public const string FixtureName = "source-lookup.json";

    private readonly Dictionary<(string Kind, string Value), SourceLookupResult> _entries;

    public FakeSourceLookup()
    {
        var path = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", FixtureName);
        var rows = JsonSerializer.Deserialize<List<SourceLookupFixtureEntry>>(File.ReadAllText(path), JsonSerializerOptions.Web)
                   ?? throw new InvalidOperationException($"{FixtureName} is empty.");
        _entries = rows.ToDictionary(r => (r.Kind, r.Value), r => r.Result);
    }

    public Task<SourceLookupResult> LookupAsync(string kind, string value, CancellationToken cancellationToken)
    {
        if (!SourceLookupKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown lookup kind '{kind}'.", nameof(kind));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.GetValueOrDefault((kind, value)) ?? SourceLookupResult.NotFound());
    }
}

public sealed record SourceLookupFixtureEntry(string Kind, string Value, SourceLookupResult Result);
