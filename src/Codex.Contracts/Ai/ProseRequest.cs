namespace Codex.Contracts.Ai;

// What a draft is generated from. `Instruction` is the chosen template's text (may be empty), `Prompt` the
// scene beat and context the author wrote. `Model` is the model id to ask for. `IntegrationId` is the current user's AI
// integration that answers (#36); null means none is connected, and the production generator refuses with not_configured.
public sealed record ProseRequest(string Model, string Instruction, string Prompt, Guid? IntegrationId = null);
