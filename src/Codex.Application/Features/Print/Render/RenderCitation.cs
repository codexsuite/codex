namespace Codex.Application.Features.Print.Render;

// A reference use with the parts of its source a printed citation needs. Authors is CSL-JSON names.
public sealed record RenderCitation(Guid UseId, string Authors, string Title, int? Year, string? Publisher, string? Locator);
