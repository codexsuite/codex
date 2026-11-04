using Codex.Application.Events;
using Codex.Contracts.Events;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Events;

// Two listening hosts on two cloned databases of one server (as every pair of test classes is): NOTIFY is scoped to
// the database, so neither hears the other. The proof needs no wait: the server keeps one notification queue in commit
// order, so had the first host's event crossed over, it would reach the second host before the marker sent after it.
[TestFixture]
[CancelAfter(30000)]
public class BookEventsDatabaseIsolationTests : ApiFixture
{
    private ClonedDatabase _otherDatabase = null!;
    private CodexHost _other = null!;

    [OneTimeSetUp]
    public async Task StartHostOnAnotherDatabaseAsync()
    {
        _otherDatabase = await CodexDatabase.CloneSeedAsync();
        _other = CodexHost.Start(_otherDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public async Task StopHostOnAnotherDatabaseAsync()
    {
        if (_other != null)
        {
            await _other.DisposeAsync();
        }
        if (_otherDatabase != null)
        {
            await _otherDatabase.DisposeAsync();
        }
    }

    [Test]
    public async Task Publish_WhenAHostOnAnotherDatabasePublishes_ShouldNotReachThisOne()
    {
        var bookId = Guid.NewGuid();
        using var other = new EventCollector(_other.Services.GetRequiredService<IBookEvents>(), bookId);
        var marker = new StructureChanged(bookId, "scene");

        await Host.Services.GetRequiredService<IBookEvents>().PublishAsync(new StructureChanged(bookId, "chapter"));
        await using (var db = await _otherDatabase.OpenAsync())
        {
            var payload = BookEventsNotifier.Serialize(new BookEventEnvelope(Guid.NewGuid(), marker));
            await db.ExecuteAsync("SELECT pg_notify(@Channel, @payload)", new { BookEventsNotifier.Channel, payload });
        }

        (await other.NextAsync()).ShouldBe(marker);
    }
}
