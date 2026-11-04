namespace Codex.Application.Features.Import;

// A knowledge base entry on the new book. Key is how scenes refer to it (a name, or a KSUID for Novelcrafter);
// ExternalId lands in kb_entry.external_id and Aliases in kb_entry.aliases. Id is set when prose already names the
// entry (::spell{kb="Id"}); Fields are kb_entry.fields (keys of the category's field_defs). ImportUrl is where the text came
// from (kb_entry.import_url); AlwaysInContext is kb_entry.always_in_context.
public sealed record ImportEntryPlan(
    string Key,
    string Category,
    string Name,
    string? ExternalId,
    string? Summary,
    string? Body,
    IReadOnlyList<string>? Aliases = null,
    Guid? Id = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? ImportUrl = null,
    bool AlwaysInContext = false);
