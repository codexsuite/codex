namespace Codex.Contracts.References;

// Where in the source a use points ("pp. 41-43", "Table 5.1"). Empty text clears it.
public sealed record SetUseLocatorRequest(string? Locator);
