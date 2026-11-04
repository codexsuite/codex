using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Ai;

namespace Codex.WebApi.Tests.Ai;

// Test helpers for the generate stream (one JSON object per line).
public static class DraftStreams
{
    public static async Task<HttpResponseMessage> OpenAsync(HttpClient client, string path, GenerateDraftRequest request, CancellationToken cancellationToken = default)
        => await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) },
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    public static async Task<DraftStreamEvent?> NextAsync(StreamReader reader, CancellationToken cancellationToken = default)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        return line == null ? null : JsonSerializer.Deserialize<DraftStreamEvent>(line, JsonSerializerOptions.Web);
    }

    public static async Task<T> GetAsync<T>(HttpClient client, string path)
        => await client.GetFromJsonAsync<T>(path, JsonSerializerOptions.Web) ?? throw new InvalidOperationException($"{path} returned null.");

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The body was null.");

    public static async Task<List<DraftStreamEvent>> ReadAllAsync(HttpResponseMessage response)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var events = new List<DraftStreamEvent>();
        while (await NextAsync(reader) is { } streamEvent)
        {
            events.Add(streamEvent);
        }

        return events;
    }
}
