using System.Text.Json;

namespace Codex.Contracts.References;

// A cited source as CSL-JSON, for citeproc. Status is the source's clearance status, so the bibliography can mark
// the ones still at risk. CreditLine is the credit a granted permission asks to print after the entry.
public sealed record SourceCslDto(Guid SourceId, string Status, JsonElement Csl, string? CreditLine);
