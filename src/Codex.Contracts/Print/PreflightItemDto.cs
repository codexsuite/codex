namespace Codex.Contracts.Print;

// One preflight finding. Severity is block (stops a final export), warn or note. Kind is low_dpi, raw_latex,
// unknown_directive, missing_asset, missing_entry, missing_reference or copyright_risk. SceneId says where it sits
// (null for a copyright risk, which is about a source); Detail is what it is.
public sealed record PreflightItemDto(string Severity, string Kind, string Detail, Guid? SceneId, Guid? ReferenceId);
