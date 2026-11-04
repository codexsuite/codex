using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The references half of the seed (#16 Part 1, 1.6). Statuses are never stored, so everything here is read from
// reference_clearance and book_reference_summary. Read-only against one clone.
[TestFixture]
public class SeedReferenceTests
{
    private ClonedDatabase _database = null!;

    public sealed record ClearanceRow(string Title, int? Number, string Status, string Reason, int QuotedWords, int Uses);

    public sealed record SummaryRow(int SourceCount, int RiskCount, int NeedsCheckCount, int ClearedCount, int NotCitedCount, int OpenQuoteFlags, bool BlocksPrintExport);

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    private async Task<List<ClearanceRow>> ReadClearanceAsync()
    {
        await using var connection = await _database.OpenAsync();
        return (await connection.QueryAsync<ClearanceRow>(
            """
            SELECT r.title AS Title, c.number::int AS Number, c.status AS Status, c.reason AS Reason,
                   c.quoted_words + c.lyric_words AS QuotedWords, c.use_count AS Uses
            FROM reference_clearance c
            JOIN reference r ON r.id = c.reference_id
            WHERE c.book_id = @book
            ORDER BY c.number NULLS LAST, r.title
            """, new { book = SeedIds.Books.ProductEngineering })).ToList();
    }

    [Test]
    public async Task Clearance_WhenSeeded_ShouldShowFourOfElevenCleared()
    {
        var rows = await ReadClearanceAsync();

        var cited = rows.Where(r => r.Status != "not_cited").ToList();
        cited.Count.ShouldBe(11);
        cited.Count(r => r.Status == "cleared").ShouldBe(4);
        rows.Single(r => r.Title == "Lean Software Development").Status.ShouldBe("not_cited");
        rows.Count.ShouldBe(12);
    }

    [Test]
    public async Task Clearance_WhenSeeded_ShouldShowThreeCopyrightRisks()
    {
        var rows = await ReadClearanceAsync();
        await using var connection = await _database.OpenAsync();
        var summary = await connection.QuerySingleAsync<SummaryRow>(
            """
            SELECT source_count AS SourceCount, risk_count AS RiskCount, needs_check_count AS NeedsCheckCount,
                   cleared_count AS ClearedCount, not_cited_count AS NotCitedCount, open_quote_flags AS OpenQuoteFlags,
                   blocks_print_export AS BlocksPrintExport
            FROM book_reference_summary WHERE book_id = @book
            """, new { book = SeedIds.Books.ProductEngineering });

        rows.Where(r => r.Status == "risk").Select(r => r.Title)
            .ShouldBe(["The Velocity Trap", "Team Topologies", "Ship It Friday"], ignoreOrder: true);
        summary.ShouldBe(new SummaryRow(12, 3, 4, 4, 1, 2, true));
    }

    [Test]
    public async Task VelocityTrap_ShouldCount342Of300Words()
    {
        await using var connection = await _database.OpenAsync();

        var quoted = await connection.ExecuteScalarAsync<int>(
            "SELECT quoted_words FROM reference_clearance WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap });
        var limit = await connection.ExecuteScalarAsync<int>(
            "SELECT quote_word_limit FROM book_publication_settings WHERE book_id = @id", new { id = SeedIds.Books.ProductEngineering });
        var perUse = (await connection.QueryAsync<(Guid Id, int Words)>(
            "SELECT id AS Id, word_count AS Words FROM reference_use WHERE reference_id = @id ORDER BY word_count DESC",
            new { id = SeedIds.References.VelocityTrap })).ToList();

        quoted.ShouldBe(342);
        limit.ShouldBe(300);
        perUse.ShouldBe([(SeedIds.ReferenceUses.VelocityTrapCh4Sc1, 214), (SeedIds.ReferenceUses.VelocityTrapCh8Sc1, 128)]);
    }

    [Test]
    public async Task EveryStatus_ShouldMatchIssue16()
    {
        var rows = await ReadClearanceAsync();

        rows.ShouldBe(
        [
            new("Extreme Programming Explained", 1, "cleared", "signed_off", 0, 1),
            new("Manifesto for Agile Software Development", 2, "needs_check", "fair_use_undecided", 7, 1),
            new("Problems of Monetary Management: The U.K. Experience", 3, "needs_check", "ready_to_sign_off", 17, 2),
            new("The Velocity Trap", 4, "risk", "quote_over_limit", 342, 2),
            new("How Do Committees Invent?", 5, "cleared", "signed_off", 0, 2),
            new("Empowered", 6, "cleared", "signed_off", 0, 1),
            new("2024 State of DevOps Report", 7, "needs_check", "licence_unknown", 0, 1),
            new("Accelerate", 8, "needs_check", "new_uses_since_signoff", 0, 2),
            new("Team Topologies", 9, "risk", "permission_sent", 0, 1),
            new("The Mythical Man-Month", 10, "cleared", "signed_off", 10, 1),
            new("Ship It Friday", 11, "risk", "lyric", 13, 1),
            new("Lean Software Development", null, "not_cited", "no_markers", 0, 0),
        ]);
    }

    [Test]
    public async Task Accelerate_ShouldHaveExactlyOneUseNewerThanItsSignOff()
    {
        await using var connection = await _database.OpenAsync();

        var newer = (await connection.QueryAsync<Guid>(
            """
            SELECT u.id FROM reference_use u JOIN reference r ON r.id = u.reference_id
            WHERE r.id = @id AND u.changed_at > r.signed_off_at
            """, new { id = SeedIds.References.Accelerate })).ToList();

        newer.ShouldBe([SeedIds.ReferenceUses.AccelerateCh5Sc2]);
    }

    [Test]
    public async Task QuoteFlags_ShouldBeOpenAtCh6Sc1AndCh8Sc1()
    {
        await using var connection = await _database.OpenAsync();

        var flags = (await connection.QueryAsync<(Guid Id, Guid Scene, string Status, string Reason, double? Score, string? Locator, Guid? Reference)>(
            "SELECT id, scene_id, status, reason, match_score::float8, locator, reference_id FROM quote_flag ORDER BY created_at, id"))
            .OrderBy(f => f.Scene == SeedIds.Scenes.Ch6Sc1 ? 0 : 1).ToList();

        flags.Count.ShouldBe(2);
        flags[0].ShouldBe((SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, SeedIds.Scenes.Ch6Sc1, "open", "quotation_marks", null, null, null));
        flags[1].Id.ShouldBe(SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch);
        flags[1].Scene.ShouldBe(SeedIds.Scenes.Ch8Sc1);
        flags[1].Status.ShouldBe("open");
        flags[1].Reason.ShouldBe("source_match");
        flags[1].Score!.Value.ShouldBe(0.93, 0.001);
        flags[1].Locator.ShouldBe("p. 112");
        flags[1].Reference.ShouldBe(SeedIds.References.VelocityTrap);
        var passage = await connection.ExecuteScalarAsync<string>("SELECT excerpt FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch });
        passage!.ShouldStartWith("Teams that track velocity");
    }

    [Test]
    public async Task TeamTopologies_ShouldHaveOneSentRequestWithNoReply()
    {
        await using var connection = await _database.OpenAsync();

        var request = await connection.QuerySingleAsync<(Guid Id, string Status, bool SentLongAgo, DateTimeOffset? RespondedAt)>(
            "SELECT id, status, sent_at < now() - interval '10 days', responded_at FROM permission_request WHERE reference_id = @id",
            new { id = SeedIds.References.TeamTopologies });

        request.Id.ShouldBe(SeedIds.PermissionRequests.TeamTopologies);
        request.Status.ShouldBe("sent");
        request.RespondedAt.ShouldBeNull();
        request.SentLongAgo.ShouldBeTrue();
    }
}
