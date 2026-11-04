using Codex.Application.Features.References;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// One scene's quote through its life: flagged, cited by a wrapping use, unmarked again, deleted. Each scan closes the
// scene's open flags whose quote is no longer unmarked; a "Not a quote" flag is never touched.
[TestFixture]
public class QuoteFlagStaleTests : SchemaApiFixture
{
    private const string Quote = "we never ship a release on a Friday afternoon";
    private static Guid _bookId;
    private static Guid _sceneId;
    private static Guid _useId;

    private Task ScanAsync() => Host.Services.GetRequiredService<QuoteFlagScanner>().ScanAsync(_bookId, _sceneId);

    private async Task<(string Status, Guid? UseId)[]> FlagsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return (await db.QueryAsync<(string, Guid?)>(
            "SELECT status, resolved_use_id FROM quote_flag WHERE scene_id = @_sceneId AND excerpt = @Quote",
            new { _sceneId, Quote })).ToArray();
    }

    private async Task SetProseAsync(string body)
    {
        await using var db = await OpenDatabaseAsync();
        await TestRows.SetProseAsync(db, _sceneId, body);
    }

    [OneTimeSetUp]
    public async Task CreateSceneAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneId, $"One lead insists \"{Quote}\" every sprint.\n");
    }

    [Test, Order(1)]
    public async Task Scan_WhenTheQuoteIsUnmarked_ShouldOpenAFlag()
    {
        await ScanAsync();

        (await FlagsAsync()).ShouldBe([("open", null)]);
    }

    [Test, Order(2)]
    public async Task Scan_WhenAUseWrapsTheQuote_ShouldMarkTheFlagCited()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            var sourceId = await TestRows.SourceAsync(db, _bookId, "Release notes");
            _useId = await TestRows.UseAsync(db, _bookId, sourceId, _sceneId, $"\"{Quote}\"");
        }

        await SetProseAsync($"One lead insists [\"{Quote}\"](ref:{_useId}) every sprint.\n");
        await ScanAsync();

        (await FlagsAsync()).ShouldBe([("cited", _useId)]);
    }

    [Test, Order(3)]
    public async Task Scan_WhenTheMarkerIsRemoved_ShouldReopenTheFlag()
    {
        await SetProseAsync($"One lead insists \"{Quote}\" every sprint.\n");
        await ScanAsync();

        (await FlagsAsync()).ShouldBe([("open", null)]);
    }

    [Test, Order(4)]
    public async Task Scan_WhenTheQuoteIsDeleted_ShouldDropTheOpenFlag()
    {
        await SetProseAsync("One lead insists on shipping early in the week.\n");
        await ScanAsync();

        (await FlagsAsync()).ShouldBeEmpty();
    }

    [Test, Order(5)]
    public async Task Scan_WhenADismissedQuoteIsGone_ShouldKeepItDismissed()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync(
                "INSERT INTO quote_flag (book_id, scene_id, excerpt, reason, status) VALUES (@_bookId, @_sceneId, @Quote, 'quotation_marks', 'dismissed')",
                new { _bookId, _sceneId, Quote });
        }

        await ScanAsync();

        (await FlagsAsync()).ShouldBe([("dismissed", null)]);
    }
}
