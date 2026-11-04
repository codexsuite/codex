# AI drafts (Write)

A draft is an `ai_generation` row. Endpoints live in `WebApi/Features/Ai/DraftsController.cs`; the TS client is in `@codex/core/api/write` (hooks in `src/api/writeQueries.ts`).

| Call | Does |
|---|---|
| `GET /api/books/{id}/drafts` | `BookDraftsDto`: the latest generation of each live beat (and of each scene's continuation) when its status is `pending` |
| `POST /api/beats/{id}/generate` | `GenerateDraftRequest { templateId?, model, targetWords?, retryOfId?, integrationId? }`, answers an NDJSON stream |
| `POST /api/scenes/{id}/continue` | the same request and stream, from the end of the scene (no beat; kind `continue`) |
| `POST /api/generations/{id}/apply` | `{ version }` -> `{ version, body }`. Replaces the prose under the `::beat` line (every block up to the next `::beat` or the end of the scene; embeds in it go with it and Undo apply brings them back) (a continuation: appends at the end of the scene) through `IProseStore.UpdateAsync` with origin `ai_apply`, so a `prose_revision` with `generation_id` is cut. 409 for a stale version or a draft that is not pending |
| `POST /api/generations/{id}/undo-apply` | `{ version }` -> `{ version, body }`. Puts the pre-apply body back (origin `restore`); the draft is pending again |
| `POST /api/generations/{id}/discard`, `.../undo-discard` | `DraftDto`; 409 when the draft is not in the expected status |

## The generate stream

`Content-Type: application/x-ndjson`: one JSON object per line, null members omitted (`DraftStreamEvent`). Read it with `api.stream` and `draftEvents`.

```
{"started":{"generationId":"…","attempt":1,"model":"…","templateId":"…","template":"Paraphrase","targetWords":400}}
{"text":"Some words. "}                                  (any number of these)
{"final":{"generationId":"…","model":"…","inputTokens":12,"outputTokens":9,"words":7}}
```

- A generator that fails after the stream began ends it with `{"error":{"error":"…","code":"internal_error"}}`; the row is `failed`.
- A failure before the first chunk (including `not_configured`, 503) is an ordinary HTTP error, not a stream line; the row is `failed`.
- Stop is the client aborting the request. The server keeps the text received so far as a `pending` draft (no text: `discarded`).
- Retry: send `retryOfId`. The attempt is the old one plus one; the old draft is discarded once the new one is under way.
- The prompt is built from the template text, the book/series style guide, the beat (or "continue"), its context entries plus the
  always-in-context entries, and the scene's prose before the beat.

## Source uses and revisions

- A draft can carry `reference_use` rows (`generation_id` set). `ai_generation_resolve_uses` archives or unlinks them on Discard and Apply,
  so both keep the live ids in `request.sourceUses`; Undo links them to the draft again and un-archives them.
- Apply and Undo apply are one transaction each: `IProseStore.UpdateAsync` with `ProseWriteOptions` stamps
  `prose_revision.generation_id` on the `ai_apply` revision and moves the generation's status in the same transaction
  (`InTransaction`), so a draft resolved meanwhile (409) leaves the scene untouched.

- A pending beat draft whose beat already has prose shows "Apply replaces the N words below. The current text is kept in history." (N counts the prose under the beat in the cached scene body; Apply cuts a `prose_revision` of the replaced body, stamped just before the `ai_apply` one, unless a revision already holds exactly that body).

## The provider

Drafts are written with the signed-in user's own AI integration (#36; set up in Settings → AI, see [ai-integrations.md](ai-integrations.md)).
There is no server-wide key: `CODEX_ANTHROPIC_API_KEY` is gone, and one exported on the host changes nothing.

- `IProseGenerator` is `IntegrationProseGenerator` (`Features/Ai/`, registered by `AiServices`). `DraftGenerator` resolves the integration after the
  ownership checks (`IAiIntegrationResolver`: the request's `integrationId`, which must be the user's own or it is 404 `AI integration not found.`,
  else the user's default) and puts its id in `ProseRequest.IntegrationId`; the generator decrypts the key and streams through `IAiProviders`
  (openai-compatible, OpenRouter or Anthropic, per the integration's kind). Text deltas become `ProseChunk`s; the last chunk carries the model and the
  input/output tokens, which `DraftGenerator` writes into `ai_generation`. Stop is the caller's cancellation (the partial text stands).
- `GenerateDraftRequest.integrationId` is optional (the Write panel's picker sends it); `ai_generation.integration_id` records which account wrote a draft
  (kept when the integration is later removed: removing archives it).
- **Model:** `model` trimmed; empty means the integration's default model; if there is none either, 400 `Choose a model for this draft.`. A model is
  free text per integration (non-empty, at most 200 characters, no control characters): `prompts/models.json` only suggests.
- **No integration:** the request is still created, then the generator answers `not_configured` (503, before the stream, row `failed`):
  `Connect an AI provider in Settings → AI to write drafts.`
- **Use and errors:** a finished stream stamps the integration's `last_used_at` and clears `last_error`; a provider failure (an `ApiException` with
  fixed wording, never the provider's body or the key) sets `last_error` to its message and is rethrown; a Stop records nothing.

The key is never logged, put in a message, a stream line, an `ai_generation` row or an MCP result. Failures use fixed wording: 401/403
`not_configured` (the key was rejected), 402 billing `not_configured` with its own message, 429 / 5xx / network failures and a draft cut off by the
length limit `internal_error` with a specific message, a refusal `blocked`.

Tests: the draft tests substitute `IAiProviders` with `RecordingAiProviders` (`Codex.TestSupport/Fakes`; it records the connection it was given) and
insert integration rows with `AiIntegrationRows` (key encrypted by the host's own protector); the Anthropic provider tests replay hand-written
transcripts in `Codex.TestSupport/Fixtures/ai/*.sse|json` through `AnthropicStubHandler`, never a real API. Compose sets `CODEX_DATA_PROTECTION_DIR`
(the key ring) on both `codex` and `worker`.

## Prompt templates (Settings → AI, J22)

Settings → AI (`/settings/ai`) has a **Prompt templates** section under the AI providers. Which model writes a draft is not a template
matter: it is the integration and model picked in the Write draft panel ([ai-integrations.md](ai-integrations.md)). A template only decides
what the model is asked.

`GET /api/prompts` is the Write picker's list: the built-in templates (`prompts/templates/*.md`, read-only, `builtIn: true`) plus the
signed-in user's own (`prompt_template`, `UserPrompts`); its `models` are only the label suggestions from `prompts/models.json`. Everything
is per user (`user_id`); another user's template id is 404 on these routes and an unknown template (400) when generating.

| Call | Does |
|---|---|
| `GET /api/ai/templates` | `AiTemplatesDto`: every template with its text (a built-in's is its instruction) and the variable names |
| `POST /api/ai/templates`, `PUT/DELETE /api/ai/templates/{id}` | the user's own template; DELETE archives it (`archived_at`), frees its name and makes its id 404. Name 1-100 characters, unique per user (409) and different from a built-in (409); an empty body is 400. A built-in id is 400 "Duplicate it, then edit the copy." |
| `POST /api/ai/templates/{id}/duplicate` | copies a built-in (its instruction plus the variables a draft needs) or one of yours under a new name |
| `GET /api/generations/{id}/sent` | `DraftSentDto` ("Show what was sent" on a pending draft): template name, model, integration label, context entry names, tokens, and the instruction and prompt exactly as sent |

- A user's template body is the whole prompt: `{{beat}}`, `{{context}}`, `{{style_guide}}`, `{{previous_prose}}` and `{{length}}` are filled in
  per draft (`Codex.Contracts.Prompts.PromptVariables`), and the instruction sent is empty. A built-in template is an instruction before the
  standard prompt, as before.
- `DraftGenerator` (feature `Ai`) resolves the request's `templateId` through `IPromptTemplates` (`Codex.Contracts.Prompts`, implemented by
  `UserPrompts` in feature `Prompts`), so neither feature references the other.
- What was sent comes from `ai_generation.request` (`template`, `integration`, `context`, `instruction`, `prompt`), written when the draft starts,
  so editing a template later does not change it. The integration's label is recorded; its key never is.
