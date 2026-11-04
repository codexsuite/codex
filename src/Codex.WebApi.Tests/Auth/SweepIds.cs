namespace Codex.WebApi.Tests.Auth;

// The rows of JD's a sweep's routes name beyond the seed's fixed ids, inserted into that fixture's own database.
public sealed record SweepIds(
    Guid ImportId, Guid JobId, Guid ProofId, Guid TokenId, Guid DeviceSessionId, Guid TemplateId, Guid TemplateVersionId, Guid IntegrationId);
