namespace Codex.Contracts.Sharing;

// The grants on the book's series: they reach every book in it, including books added later.
public sealed record SharingSeriesDto(Guid Id, string Title, IReadOnlyList<SharingGrantDto> Grants);
