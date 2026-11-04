using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// duplicate_book() copies an asset's rows and rendition rows but not its job, and both books name the same stored
// files. Making the copy's renditions again must leave the original's files alone, and the other way round.
[TestFixture]
public class AssetCopyTests : AssetApiFixture
{
    private AssetDto _original = null!;
    private Guid _copyId;
    private Dictionary<string, string> _originalKeys = null!;
    private readonly Dictionary<string, byte[]> _originalBytes = [];

    private async Task<Dictionary<string, string>> KeysAsync(Guid assetId)
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<(string Purpose, string Key)>(
            "SELECT purpose, storage_key FROM asset_rendition WHERE asset_id = @assetId", new { assetId })).ToDictionary(r => r.Purpose, r => r.Key);
    }

    [Test, Order(1)]
    public async Task DuplicatedBook_ShouldCopyTheAssetAsReadyEvenWithoutAJob()
    {
        _original = await SettledAsync((await UploadOkAsync(SeedIds.Books.PlayersGuide, "map.png", TestImages.Photo(300, 200), "image/png")).Id);
        _originalKeys = await KeysAsync(_original.Id);
        foreach (var rendition in _original.Renditions)
        {
            _originalBytes[rendition.Purpose] = await Client.GetByteArrayAsync(rendition.Url);
        }

        await using (var connection = await OpenDatabaseAsync())
        {
            var copyBook = await connection.ExecuteScalarAsync<Guid>("SELECT duplicate_book(@id, 'Players Guide copy')", new { id = SeedIds.Books.PlayersGuide });
            _copyId = await connection.ExecuteScalarAsync<Guid>(
                "SELECT id FROM asset WHERE book_id = @copyBook AND copied_from_id = @id", new { copyBook, id = _original.Id });
            (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM asset_rendition_job WHERE asset_id = @id", new { id = _copyId })).ShouldBe(0);
        }

        var copy = (await Client.GetFromJsonAsync<AssetDto>($"/api/assets/{_copyId}/info"))!;

        copy.Status.ShouldBe("ready");
        copy.Renditions.Select(r => r.Purpose).ShouldBe(["web", "proxy", "print"]);
        (await KeysAsync(_copyId)).OrderBy(k => k.Key).ShouldBe(_originalKeys.OrderBy(k => k.Key)); // the same stored files
    }

    [Test, Order(2)]
    public async Task RequeueOfTheCopy_ShouldNotDeleteTheOriginalsFiles()
    {
        using var queued = await Client.PostAsync($"/api/assets/{_copyId}/renditions", null);
        queued.EnsureSuccessStatusCode();
        var copy = await SettledAsync(_copyId);
        var blobs = Host.Services.GetRequiredService<IBlobStore>();

        copy.Status.ShouldBe("ready");
        var copyKeys = await KeysAsync(_copyId);
        copyKeys.Values.Intersect(_originalKeys.Values).ShouldBeEmpty();
        (await KeysAsync(_original.Id)).OrderBy(k => k.Key).ShouldBe(_originalKeys.OrderBy(k => k.Key));
        foreach (var rendition in _original.Renditions)
        {
            (await blobs.ExistsAsync(_originalKeys[rendition.Purpose], CancellationToken.None)).ShouldBeTrue();
            (await Client.GetByteArrayAsync(rendition.Url)).ShouldBe(_originalBytes[rendition.Purpose]);
        }
    }

    [Test, Order(3)]
    public async Task RequeueOfTheOriginal_ShouldKeepTheCopysFilesAndDropItsOwnOldOnes()
    {
        var copyKeys = await KeysAsync(_copyId);
        using var queued = await Client.PostAsync($"/api/assets/{_original.Id}/renditions", null);
        queued.EnsureSuccessStatusCode();
        await SettledAsync(_original.Id);
        var blobs = Host.Services.GetRequiredService<IBlobStore>();

        foreach (var key in copyKeys.Values)
        {
            (await blobs.ExistsAsync(key, CancellationToken.None)).ShouldBeTrue();
        }
        foreach (var key in _originalKeys.Values)
        {
            (await blobs.ExistsAsync(key, CancellationToken.None)).ShouldBeFalse(); // no row names them any more
        }
    }
}
