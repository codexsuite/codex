# AI integrations (#36)

Each user connects their own AI provider account (Settings → AI). Drafts use the integration the request names, else the user's
default. Server-wide keys do not exist. Routes live in `WebApi/Features/Ai/AiIntegrationsController.cs`; the TS client is `@codex/core/api/ai`.
Everything is scoped to the signed-in user (`owner_id`): another user's integration is `not_found`.

Settings → AI also has a **Prompt templates** section (J22) under the providers: the built-in templates and the user's own, which decide
what a draft asks, not which model answers. Its routes (`/api/ai/templates`) and "Show what was sent" are in
[ai-drafts.md](ai-drafts.md#prompt-templates-settings--ai-j22).

| Call | Does |
|---|---|
| `GET /api/ai/integrations` | `AiSettingsDto`: the user's integrations (default first, then in connected order) and `localEndpointsAllowed` |
| `POST /api/ai/integrations` | `CreateAiIntegrationRequest { kind, label, baseUrl?, apiKey, defaultModel? }` -> `AiIntegrationDto`. The user's first becomes the default |
| `PATCH /api/ai/integrations/{id}` | `UpdateAiIntegrationRequest { label?, baseUrl?, apiKey?, defaultModel? }`. `null` leaves a member alone; a new `apiKey` replaces the key; `defaultModel: ""` clears the model |
| `DELETE /api/ai/integrations/{id}` | 204. Archives the row (`archived_at`), blanks its stored key and clears its default flag; it no longer lists, drafts, tests or takes edits (404). Removing the default makes the oldest remaining live one the default; drafts keep their `integration_id` |
| `POST /api/ai/integrations/{id}/default` | `AiSettingsDto` with `{id}` as the only default |
| `POST /api/ai/integrations/test` | `TestAiConnectionRequest { kind, baseUrl?, apiKey }` -> `AiConnectionTestDto`; nothing is saved |
| `POST /api/ai/integrations/{id}/test` | the same with the stored key; success caches the models in `ai_integration.models` and clears `last_error`, failure sets `last_error` |
| `GET /api/ai/models` | `AiModelChoicesDto`: the Write draft panel's picker |

A failed provider call is a normal answer (`ok: false`, `error`, fixed wording such as "the key was rejected"); a refused base URL or a
bad request is a 400 `validation`.

## Kinds

| `kind` | Base URL | Notes |
|---|---|---|
| `openai_compatible` | required | Any Chat Completions gateway (OpenAI, Azure-style gateways, Ollama, LM Studio, vLLM). Stored without a trailing slash |
| `openrouter` | refused | Fixed endpoint |
| `anthropic` | refused | Fixed endpoint |

Label 1-100 characters, key 1-1000 (both trimmed), default model up to 200. Models are fetched from the provider's `/models` on Test
connection; any model id may still be typed.

## How keys are stored

- `ai_integration.credential` is the key encrypted with ASP.NET Core Data Protection (`AiCredentialProtector`, purpose
  `Codex.AiIntegration.Credential.v1`). The browser only ever gets `keyHint` (`sk-…abcd`).
- The key ring is a folder of key files named by **`CODEX_DATA_PROTECTION_DIR`** (relative paths resolve against the content root;
  dev: `artifacts/data-protection`). It is separate from the app's Postgres-backed cookie key ring, so a database dump alone cannot
  decrypt a key. The web app, the worker and the stdio MCP host must all point at the same folder, and the folder is backed up with the
  database: lose it and every saved key must be entered again (a Test or draft then answers `not_configured`).
- Without the setting the server still starts; saving or using a key answers `not_configured` naming it.
- A rotated ring still decrypts older credentials; keys are never revoked by the app.
- The key is never returned, logged, put in an error message, an `ai_generation` row or an MCP result.

## Base URL rules (SSRF)

`AiEndpointGuard` runs when an integration is saved, edited or tested, and the providers run it again before every request, and the
HTTP clients re-check every address they connect to (they resolve the host themselves, refuse proxies and never follow redirects), so a changed DNS answer gets nowhere.
By default a base URL must be:

- `https://`, with no user name or password, query string or `#fragment`;
- a host whose addresses are **all** public (`PublicAddress.IsPublic`): no private (10/8, 172.16/12, 192.168/16), loopback,
  link-local (169.254.169.254, the cloud metadata address), carrier-grade NAT, multicast or IPv6-embedded-IPv4 address.

The server owner can set **`CODEX_AI_ALLOW_LOCAL_ENDPOINTS=true`** (read on every call) to allow `http://` and private addresses, for
Ollama or LM Studio on the LAN. The credential, query and fragment rules still apply. `AiSettingsDto.localEndpointsAllowed` tells the
page which rules are on. `IHostResolver` and `PublicAddress` live in `Codex.Application.Network`, shared with the References look-up.
