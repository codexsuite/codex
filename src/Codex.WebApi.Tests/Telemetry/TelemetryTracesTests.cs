using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Codex.Application.Telemetry;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;

namespace Codex.WebApi.Tests.Telemetry;

// What the app traces and what it keeps out of the traces, seen through the in-memory exporter on the app's own tracer
// provider (TelemetryCapture.AddTo), so this is exactly what production would export. Not parallel: the endpoint goes in
// through the process environment, which every host starting at the same time would see too. One host per test, so each
// test sees only its own spans.
[TestFixture]
[NonParallelizable]
public class TelemetryTracesTests
{
    // Nothing listens there: export fails, and the tests don't care.
    private const string DeadEndpoint = "http://127.0.0.1:1";

    private ClonedDatabase _database = null!;
    private ProcessEnvironment _environment = null!;
    private TelemetryCapture _capture = null!;
    private CodexHost _host = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task CloneDatabaseAsync() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropDatabaseAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
    }

    [SetUp]
    public void StartHost()
    {
        _environment = new ProcessEnvironment().Set("OTEL_EXPORTER_OTLP_ENDPOINT", DeadEndpoint).Set("OTEL_SERVICE_NAME", null);
        _capture = new TelemetryCapture();
        _host = CodexHost.Start(_database.ConnectionString, _capture.AddTo);
        _client = _host.CreateClient();
    }

    // The test's own HttpClient lives in the same process, so the HTTP client instrumentation would trace it too: as a root
    // client span, which is dropped, and its unsampled traceparent would then drop the server span. A caller in front of
    // the real app (Cloudflare, Caddy) is another process. Suppressed, the request reaches the server as it would there.
    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var suppress = SuppressInstrumentationScope.Begin();
        return await _client.GetAsync(path);
    }

    [TearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        _capture.Dispose();
        _environment.Dispose();
    }

    [Test]
    public async Task Request_WhenItReadsPostgres_ShouldExportOneServerSpanByRouteTemplateWithNpgsqlChildren()
    {
        var response = await GetAsync("/api/books/00000000-0000-0000-0000-000000000001?token=query-secret-value");
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);

        var spans = await ExportedAsync(isReady: a => a.Any(s => s.Kind == ActivityKind.Server));
        var servers = spans.Where(a => a.Kind == ActivityKind.Server).ToArray();
        var server = servers.ShouldHaveSingleItem();
        server.GetTagItem("http.route").ShouldBe("/api/books/{id:guid}");
        server.DisplayName.ShouldBe("GET /api/books/{id:guid}");
        server.GetTagItem("url.path").ShouldBeNull();
        server.GetTagItem("url.query").ShouldBeNull();

        var database = spans.Where(a => a.Source.Name == "Npgsql").ToArray();
        database.ShouldNotBeEmpty();
        database.ShouldAllBe(a => IsDescendant(a, server, spans));
        AllTagValues(spans).ShouldAllBe(value => !value.Contains("query-secret-value", StringComparison.Ordinal));
        AllTagValues(spans).ShouldAllBe(value => !value.Contains("00000000-0000-0000-0000-000000000001", StringComparison.Ordinal));
    }

    [Test]
    public async Task Request_WhenItIsAListThatReadsPostgres_ShouldRecordSqlTextButNoParameterValues()
    {
        var response = await GetAsync("/api/library");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var spans = await ExportedAsync(isReady: a => a.Any(s => s.Kind == ActivityKind.Server));
        var statements = spans.Where(a => a.Source.Name == "Npgsql")
            .Select(a => (a.GetTagItem("db.query.text") ?? a.GetTagItem("db.statement"))?.ToString())
            .OfType<string>().ToArray();
        statements.ShouldNotBeEmpty();
        // The seed's user id is a parameter of the access query: it must not appear as a literal anywhere.
        AllTagValues(spans).ShouldAllBe(value => !value.Contains(TestUser.Jd.Id.ToString()!, StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task Request_WhenForAnSpaAssetTheHealthCheckOrTheHub_ShouldExportNoSpanButTheApiRequestAfterThemDoes()
    {
        foreach (var path in new[] { "/index.html", "/assets/app.js", "/mobile-editor.html", "/login", "/health", "/api/health", "/hubs/book/negotiate?negotiateVersion=1" })
        {
            using var response = await GetAsync(path);
        }

        // Spans end just after the response is sent, so wait for the API request's own before counting. The earlier
        // requests ended long before it did, so if they had made spans, they would be there by now.
        using var api = await GetAsync("/api/library");

        var spans = await ExportedAsync(isReady: a => a.Any(s => s.Kind == ActivityKind.Server));
        spans.Where(a => a.Kind == ActivityKind.Server).ShouldHaveSingleItem().GetTagItem("http.route").ShouldBe("/api/library");
    }

    [Test]
    public async Task HttpClientCall_WhenItCarriesAnAuthorizationHeaderAndUrlCredentials_ShouldRecordNeitherAndNoQuery()
    {
        // Inside a job or a request span, as outgoing calls are in the app: a client span with no trace around it is dropped.
        using var parent = CodexTelemetry.Source.StartActivity("test.parent");
        using var http = _host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_host.BaseUrl.Replace("http://", "http://provider-user:provider-pass@", StringComparison.Ordinal)}/health?key=provider-query-key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "provider-bearer-secret");
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var spans = await ExportedAsync(isReady: a => a.Any(s => s.Kind == ActivityKind.Client));
        var call = spans.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == "System.Net.Http").ShouldHaveSingleItem();
        call.GetTagItem("url.full").ShouldBe($"{_host.BaseUrl}/health");
        foreach (var value in AllTagValues(spans))
        {
            value.ShouldNotContain("provider-bearer-secret");
            value.ShouldNotContain("provider-user");
            value.ShouldNotContain("provider-pass");
            value.ShouldNotContain("provider-query-key");
        }

        call.TagObjects.ShouldNotContain(tag => tag.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task BackgroundDatabaseWork_WhenNoRequestOrJobSpanIsAround_ShouldExportNothingButTheSameWorkInsideARequestSpanIs()
    {
        // A root Npgsql command: what the polling workers and the book-event listener do all day.
        await using (var connection = await _host.Services.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "select 1";
            await command.ExecuteScalarAsync();
        }

        Exported().ShouldBeEmpty();

        using var parent = CodexTelemetry.Source.StartActivity("test.parent");
        await using (var connection = await _host.Services.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "select 2";
            await command.ExecuteScalarAsync();
        }

        parent!.Stop();
        Exported().ShouldContain(a => a.Source.Name == "Npgsql" && a.ParentSpanId == parent.SpanId);
    }

    // Server spans end just after the response goes out, so the test can see the response before the span is exported.
    private async Task<Activity[]> ExportedAsync(Func<Activity[], bool> isReady)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        var spans = Exported();
        while (!isReady(spans) && await timer.WaitForNextTickAsync(timeout.Token))
        {
            spans = Exported();
        }

        return spans;
    }

    private Activity[] Exported()
    {
        lock (_capture.Activities)
        {
            return [.. _capture.Activities];
        }
    }

    private static IEnumerable<string> AllTagValues(IEnumerable<Activity> spans)
        => spans.SelectMany(a => a.TagObjects).Select(tag => tag.Value?.ToString() ?? string.Empty);

    private static bool IsDescendant(Activity child, Activity ancestor, Activity[] spans)
    {
        for (var parent = child.ParentSpanId; parent != default;)
        {
            if (parent == ancestor.SpanId)
            {
                return true;
            }

            parent = spans.FirstOrDefault(a => a.SpanId == parent)?.ParentSpanId ?? default;
        }

        return false;
    }
}
