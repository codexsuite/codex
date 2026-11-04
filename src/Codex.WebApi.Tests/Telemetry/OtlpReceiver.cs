using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Codex.WebApi.Tests.Telemetry;

// An in-process OTLP/HTTP collector: keeps the body and headers of every POST to /v1/logs, /v1/traces and /v1/metrics, and
// answers 200. Stands in for the box's collector.
internal sealed class OtlpReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<Post> _posts = new();

    private OtlpReceiver(WebApplication app) => _app = app;

    public string Address => _app.Urls.First().TrimEnd('/');

    public static async Task<OtlpReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new OtlpReceiver(app);
        app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
        {
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body);
            receiver._posts.Enqueue(new Post(signal, body.ToArray(), request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
            return Results.Ok();
        });
        await app.StartAsync();
        return receiver;
    }

    public IReadOnlyList<Post> PostsTo(string signal) => [.. _posts.Where(p => p.Signal == signal)];

    // Waits for the sink's periodic batch (a couple of seconds) to deliver a matching post.
    public async Task<Post?> WaitForPostAsync(string signal, Func<Post, bool> matches)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(timeout.Token))
            {
                var found = PostsTo(signal).FirstOrDefault(matches);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return null;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    internal sealed record Post(string Signal, byte[] Body, IReadOnlyDictionary<string, string> Headers)
    {
        public bool Contains(ReadOnlySpan<byte> bytes) => Body.AsSpan().IndexOf(bytes) >= 0;
    }
}
