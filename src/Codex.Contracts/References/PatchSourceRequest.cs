namespace Codex.Contracts.References;

// PATCH /api/references/{id}. A null part is left alone: Details replaces every editable field at once, Entry sets
// the Codex link (an EntryId of null removes it).
public sealed record PatchSourceRequest(SourceDetailsRequest? Details, SourceEntryRequest? Entry);
