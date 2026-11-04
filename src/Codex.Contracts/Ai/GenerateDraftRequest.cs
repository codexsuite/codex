namespace Codex.Contracts.Ai;

// POST /api/beats/{id}/generate and POST /api/scenes/{id}/continue. `IntegrationId` names one of the user's AI
// integrations from GET /api/ai/models, and null means the user's default. `Model` is a model id for that integration;
// empty means its default model. `TemplateId` is a template id from GET /api/prompts, and null means no template.
// `TargetWords` defaults to the beat's own length (400 for a continuation). `RetryOfId` names the generation this one
// replaces: a pending one is discarded, the attempt counts up.
public sealed record GenerateDraftRequest(string? TemplateId, string Model, int? TargetWords, Guid? RetryOfId, Guid? IntegrationId = null);
