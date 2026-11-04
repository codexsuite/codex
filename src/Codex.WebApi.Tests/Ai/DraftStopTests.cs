using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

// Stop is the client closing the request: the text received so far stays as a pending draft.
[TestFixture]
public class DraftStopTests : SchemaApiFixture
{
    private static readonly FinishSignallingGenerator Generator = new(new FakeProseGenerator(["Half a draft. ", "The rest."], new TaskCompletionSource().Task));

    private Guid _beatId;
    private Guid _bookId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        var (bookId, _, sceneId) = await TestRows.SingleSceneAsync(db);
        _bookId = bookId;
        _beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{_beatId}\"}}\n");
    }

    [Test]
    public async Task Generate_WhenTheClientStops_ShouldKeepThePartialTextAsAPendingDraft()
    {
        // Closing the response closes the connection, which is what the browser's abort does.
        var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, "claude-sonnet-5-5", null, null));
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        (await DraftStreams.NextAsync(reader))?.Started.ShouldNotBeNull();
        (await DraftStreams.NextAsync(reader))?.Text.ShouldBe("Half a draft. ");
        reader.Dispose();
        response.Dispose();

        // The server closes the row before it disposes the generator's enumerator, so this is the moment the stop is final.
        await Generator.Finished.Task;
        var draft = (await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts")).Drafts.FirstOrDefault();

        draft.ShouldNotBeNull();
        draft.Text.ShouldBe("Half a draft. ");
        draft.Status.ShouldBe("pending");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int?>("SELECT output_tokens FROM ai_generation WHERE id = @id", new { id = draft.Id })).ShouldBeNull();
    }
}

// Completes `Finished` when the server disposes the stream, which it does after it has closed the generation row.
internal sealed class FinishSignallingGenerator(IProseGenerator inner) : IProseGenerator
{
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in inner.GenerateAsync(request, cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            Finished.TrySetResult();
        }
    }
}
