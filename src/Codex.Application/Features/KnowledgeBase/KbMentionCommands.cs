using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Application.Prose;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbMentionCommands
{
    Task<KbEntryDto> MakeMentionAsync(Guid entryId, Guid sceneId, CancellationToken cancellationToken);
}

// "Make mention": the first plain whole-word use of the entry's name (else of an alias) in the scene's prose becomes
// [text](kb:id), through IProseStore.UpdateAsync. Unknown entry or scene 404; nothing that can be wrapped (inside
// code, a link or a directive, or absent) 400 validation; a scene that changes while the edit runs 409.
[RegisterSingleton]
public sealed class KbMentionCommands(NpgsqlDataSource dataSource, IProseStore proseStore, IKbEntryQueries queries, IBookAccess access) : IKbMentionCommands
{
    private sealed record Names(string Name, string Aliases);

    public async Task<KbEntryDto> MakeMentionAsync(Guid entryId, Guid sceneId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.View, cancellationToken);
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken);
        Names row;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            row = await connection.QuerySingleOrDefaultAsync<Names>(new CommandDefinition(
                "SELECT name, to_json(aliases)::text AS Aliases FROM kb_entry WHERE id = @entryId AND archived_at IS NULL",
                new { entryId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
        }
        var names = new List<string> { row.Name };
        names.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.Aliases) ?? []);

        var prose = await proseStore.GetAsync(sceneId, cancellationToken);
        await proseStore.UpdateAsync(sceneId, body => Wrap(body, names, entryId), prose.Version, "kb_mention", cancellationToken);
        return await queries.GetAsync(entryId, cancellationToken);
    }

    private static string Wrap(string body, List<string> names, Guid entryId)
    {
        var document = ProseDocument.Parse(body);
        foreach (var name in names)
        {
            try
            {
                return document.WrapMention(name, entryId).ToString();
            }
            catch (ProseEditException e) when (e.Failure == ProseEditFailure.PassageNotWrappable)
            {
                // Try the next name.
            }
        }
        throw ApiException.Validation("The scene has no plain use of this entry's name that can become a mention.");
    }
}
