using Codex.Application.Features.Ai.Providers;

namespace Codex.Application.Features.Ai.Integrations;

// An integration ready to call: its row's identity and default model, and the connection with the decrypted key.
public sealed record ResolvedAiIntegration(Guid Id, string Kind, string Label, string? DefaultModel, AiConnection Connection);
