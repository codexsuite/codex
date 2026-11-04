using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J23 variant, large book (R7): Iron Litany from db/seed-large, 30 chapters and 90 scenes. The count comes from the
// server for the whole book, and Replace all finishes on every scene that holds the word. Generous timeouts, not budgets.
[TestFixture, Category("J23")]
public class J23_FindAndReplace_LargeBook : SeededLargeAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.IronLitany.ToString();

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = await Clone.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    [Test]
    public async Task ReplaceAll_WhenTheWordIsInManyChapters_ShouldCountTheWholeBookAndReplaceItEverywhere()
    {
        var write = new WritePageObject(Page);
        // The long word that is in the most scenes: filler vocabulary, so it is all over the book.
        var word = await ScalarAsync<string>(
            $"""
            SELECT w FROM (
                SELECT DISTINCT p.scene_id, lower(x) AS w
                FROM prose p JOIN scene s ON s.id = p.scene_id, regexp_split_to_table(p.body, '[^A-Za-z]+') AS x
                WHERE s.book_id = '{SeedIds.Books.IronLitany}' AND length(x) >= 4
            ) t GROUP BY w ORDER BY count(*) DESC, w LIMIT 1
            """);
        var scenes = await ScalarAsync<long>(
            $"SELECT count(*) FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = '{SeedIds.Books.IronLitany}' AND p.body ~* '\\m{word}\\M'");
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Minimap).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceWholeWord.CheckAsync();
        await write.Find.FillAsync(word!);
        await write.ReplaceInput.FillAsync("replacementword");

        await Expect(write.ReplaceCount).ToContainTextAsync($"in {scenes} scene", new() { Timeout = 60_000 });
        scenes.ShouldBeGreaterThan(30);
        await write.ReplaceAll.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync($"in {scenes} scenes", new() { Timeout = 120_000 });
        (await ScalarAsync<long>(
            $"SELECT count(*) FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = '{SeedIds.Books.IronLitany}' AND p.body ~* '\\m{word}\\M'")).ShouldBe(0);
    }
}
