namespace Codex.Contracts.References;

// Id is the new source (201). ExistingId is set instead, with 200, when the book already has the same ISBN, DOI, URL
// or cite key: nothing was created.
public sealed record AddSourceResult(Guid? Id, Guid? ExistingId);
