using System.Text.Json.Serialization;

namespace Codex.Contracts.Events;

// Something that happened inside one book. The "type" discriminator is the wire shape the realtime hub will
// reuse (mirrored in src/Codex.Core.Client/src/api/events.ts).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProseSaved), "proseSaved")]
[JsonDerivedType(typeof(StructureChanged), "structureChanged")]
[JsonDerivedType(typeof(JobProgress), "jobProgress")]
[JsonDerivedType(typeof(BookMetaChanged), "bookMetaChanged")]
[JsonDerivedType(typeof(AccessChanged), "accessChanged")]
public abstract record BookEvent(Guid BookId)
{
    // The browser tab whose request caused the event (its X-Codex-Client header), stamped by the bus when the event
    // is published; null for a write with no such header (MCP, a job) and omitted from the JSON then. A tab ignores
    // the events it caused itself.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; init; }
}
