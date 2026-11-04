using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace Codex.Application.Features.Settings;

// Layout & print, read-only: the book's current template from book_layout_current, and the risks that block print export.
// Needs view (#80).
[RegisterSingleton]
public sealed class LayoutSettingsQueries(NpgsqlDataSource dataSource, IBookAccess access)
{
    private sealed record TemplateRow(
        string Name, string? Description, bool IsUserTemplate, string PinnedVersion, string? LatestVersion,
        bool UpgradeAvailable, string? TrimSize, string TrimsJson);

    public async Task<LayoutSettingsDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')",
            new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("That book doesn't exist.");
        }

        var row = await connection.QuerySingleOrDefaultAsync<TemplateRow>(new CommandDefinition(
            """
            SELECT c.template_name AS Name, t.description, c.is_user_template AS IsUserTemplate,
                   c.pinned_version AS PinnedVersion, c.latest_version AS LatestVersion,
                   coalesce(c.upgrade_available, false) AS UpgradeAvailable, c.trim_size AS TrimSize,
                   coalesce(c.trims, '[]'::jsonb)::text AS TrimsJson
            FROM book_layout_current c JOIN layout_template t ON t.id = c.template_id
            WHERE c.book_id = @bookId
            """,
            new { bookId }, cancellationToken: cancellationToken));
        var risks = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT risk_count FROM book_reference_summary WHERE book_id = @bookId",
            new { bookId }, cancellationToken: cancellationToken)) ?? 0;

        LayoutTemplateDto? template = null;
        if (row is not null)
        {
            var trims = JsonSerializer.Deserialize<string[]>(row.TrimsJson) ?? [];
            template = new LayoutTemplateDto(
                row.Name, row.Description, row.IsUserTemplate, row.PinnedVersion,
                row.UpgradeAvailable ? row.LatestVersion : null, row.TrimSize, trims);
        }
        return new LayoutSettingsDto(template, risks);
    }
}
