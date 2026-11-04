namespace Codex.Contracts.References;

// Where a flagged quote really comes from when the source is not in the library. Note must not be blank.
public sealed record OtherSourceRequest(string? Note);
