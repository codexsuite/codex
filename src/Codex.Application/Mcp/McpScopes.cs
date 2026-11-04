using Codex.Application.Identity;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The scope each tool needs (#32, the #20 review's split of read, write, copyright and spend), checked before the tool
// runs. A call without it fails forbidden, naming the scope. Holding codex:references never skips the author's own
// confirmation: sign_off and accept_fair_use stay UI-only (M6). undo needs codex:write here, and UndoTools also asks for
// the scope of the tool whose write it reverses. McpScopesTests fails when a tool has no row.
public static class McpScopes
{
    public static readonly IReadOnlyDictionary<string, string> ForTool = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["list_books"] = ApiTokenScopes.Read,
        ["search"] = ApiTokenScopes.Read,
        ["resume_work"] = ApiTokenScopes.Read,
        ["get_outline"] = ApiTokenScopes.Read,
        ["read_manuscript"] = ApiTokenScopes.Read,
        ["get_draft"] = ApiTokenScopes.Read,
        ["get_job"] = ApiTokenScopes.Read,
        ["get_kb_entry"] = ApiTokenScopes.Read,
        ["find_kb_usages"] = ApiTokenScopes.Read,
        ["get_book_settings"] = ApiTokenScopes.Read,
        ["get_clearance"] = ApiTokenScopes.Read,
        ["export_references"] = ApiTokenScopes.Read,
        ["list_images"] = ApiTokenScopes.Read,
        ["get_image"] = ApiTokenScopes.Read,
        ["preview_layout"] = ApiTokenScopes.Read,
        ["export_for_printer"] = ApiTokenScopes.Read,
        ["list_kb_entries"] = ApiTokenScopes.Read,
        ["list_revisions"] = ApiTokenScopes.Read,

        ["create_book"] = ApiTokenScopes.Write,
        ["update_book"] = ApiTokenScopes.Write,
        ["edit_outline"] = ApiTokenScopes.Write,
        ["edit_prose"] = ApiTokenScopes.Write,
        ["upsert_beat"] = ApiTokenScopes.Write,
        ["upsert_kb_entry"] = ApiTokenScopes.Write,
        ["link_kb_entry"] = ApiTokenScopes.Write,
        ["submit_draft"] = ApiTokenScopes.Write,
        ["resolve_draft"] = ApiTokenScopes.Write,
        ["undo"] = ApiTokenScopes.Write,
        ["upload_image"] = ApiTokenScopes.Write,
        ["create_image_upload"] = ApiTokenScopes.Write,
        ["place_figure"] = ApiTokenScopes.Write,
        ["update_figure"] = ApiTokenScopes.Write,
        ["set_opener"] = ApiTokenScopes.Write,
        ["manage_kb_category"] = ApiTokenScopes.Write,
        ["delete_kb_entry"] = ApiTokenScopes.Write,
        ["merge_kb_entries"] = ApiTokenScopes.Write,
        ["create_series"] = ApiTokenScopes.Write,
        ["update_series"] = ApiTokenScopes.Write,

        ["cite_source"] = ApiTokenScopes.References,
        ["update_source"] = ApiTokenScopes.References,
        ["record_clearance_decision"] = ApiTokenScopes.References,
        ["manage_permission"] = ApiTokenScopes.References,
        ["update_source_use"] = ApiTokenScopes.References,
        ["draft_permission_letter"] = ApiTokenScopes.References,
        ["merge_sources"] = ApiTokenScopes.References,
        ["add_source"] = ApiTokenScopes.References,
        ["archive_source"] = ApiTokenScopes.References,

        ["draft_prose"] = ApiTokenScopes.Generate,
    };

    // Fails forbidden unless the grant holds the tool's scope. A name with no row is left to the SDK (an unknown tool
    // is invalid).
    public static void Require(McpGrant grant, string? tool)
    {
        if (tool != null && ForTool.TryGetValue(tool, out var scope) && !grant.Scopes.Contains(scope))
        {
            throw new McpToolException(McpErrorCodes.Forbidden, $"{tool} needs the {scope} scope, which this token does not have.")
            {
                Field = scope,
            };
        }
    }

    // A call that needs more than its row says: preview_layout (kind "proof") and export_for_printer (mode "queue") write a job row.
    public static void RequireScope(McpGrant grant, string tool, string scope)
    {
        if (!grant.Scopes.Contains(scope))
        {
            throw new McpToolException(McpErrorCodes.Forbidden, $"{tool} needs the {scope} scope for this call, which this token does not have.")
            {
                Field = scope,
            };
        }
    }

    // Registered inside McpToolErrors.Filter, so a refusal comes back as the usual error result.
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> ToolFilter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (context, cancellationToken) =>
    {
        Require(await Access(context).CheckAsync(cancellationToken), context.Params?.Name);
        return await next(context, cancellationToken);
    };

    // Resources and prompts only read, and every token holds codex:read, so these check only that a stdio session's
    // token still works.
    public static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> ResourceFilter(
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next) => async (context, cancellationToken) =>
    {
        await CheckAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    };

    public static McpRequestHandler<GetPromptRequestParams, GetPromptResult> PromptFilter(
        McpRequestHandler<GetPromptRequestParams, GetPromptResult> next) => async (context, cancellationToken) =>
    {
        await CheckAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    };

    private static async Task CheckAsync<T>(RequestContext<T> context, CancellationToken cancellationToken)
    {
        try
        {
            await Access(context).CheckAsync(cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
    }

    private static McpAccess Access<T>(RequestContext<T> context) => context.Services!.GetRequiredService<McpAccess>();
}
