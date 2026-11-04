using Codex.Application.Mcp;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Prose;

// The guard for every scene prose save from the editor, Rich or Source: an id for a beat, Codex addition, figure or
// source reference may be kept, removed, or point at a row this scene already owns (archived or not, which is what
// cutting a line and pasting it back, or undo, relies on); it may never name a row that is not the scene's, because
// each row is created only by its own command. Nothing depends on which view typed the text, so a draft kept after a
// refusal cannot save unchecked later. Ids are read the way the database triggers do (McpProseRules.IdsIn), fences
// and quotes included. MCP keeps its stricter body-diff rule (McpProseRules.RefuseInvented).
public static class EditorProseRules
{
    // Of the ids `body` carries, the ones that are a row of this scene (archived or not). One query for all four kinds.
    public static async Task<IReadOnlyList<(string Kind, Guid Id)>> OwnedIdsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sceneId, string body, CancellationToken cancellationToken)
    {
        var ids = McpProseRules.IdsIn(body);
        if (ids.Count == 0)
        {
            return [];
        }
        var all = ids.Select(i => i.Id).Distinct().ToArray();
        var rows = await connection.QueryAsync<(string Kind, Guid Id)>(new CommandDefinition(
            """
            SELECT 'beat' AS Kind, id AS Id FROM beat WHERE scene_id = @sceneId AND id = ANY (@all)
            UNION ALL SELECT 'kb-addition', id FROM kb_progression WHERE scene_id = @sceneId AND id = ANY (@all)
            UNION ALL SELECT 'figure', id FROM figure WHERE scene_id = @sceneId AND id = ANY (@all)
            UNION ALL SELECT 'ref', id FROM reference_use WHERE scene_id = @sceneId AND id = ANY (@all)
            """,
            new { sceneId, all }, transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    // Refuses an id `after` carries that `before` did not and that is not in `owned`.
    public static void RefuseForeign(string before, string after, IReadOnlyList<(string Kind, Guid Id)> owned)
    {
        var known = McpProseRules.IdsIn(before);
        var kind = McpProseRules.IdsIn(after).Where(i => !known.Contains(i) && !owned.Contains(i)).Select(i => i.Kind).FirstOrDefault();
        var message = kind switch
        {
            null => null,
            "beat" => "That ::beat line isn't one of this scene's beats. Add a scene beat from the / menu in Rich view.",
            "kb-addition" => "That ::kb-addition line isn't one of this scene's Codex additions. Add a Codex addition from the / menu in Rich view.",
            "figure" => "That ::figure line isn't one of this scene's figures. Add a figure from the Write screen.",
            _ => "That ref: link isn't one of this scene's source references. Cite a source with Source reference from the / menu in Rich view.",
        };
        if (message != null)
        {
            throw ApiException.Validation(message);
        }
    }
}
