-- #36: per-user AI integrations. Each user connects their own provider account; drafts use the request's integration,
-- else the user's default. `credential` is the API key encrypted with ASP.NET Core Data Protection (key ring in
-- CODEX_DATA_PROTECTION_DIR, never in this database); `key_hint` ("sk-…abcd") is all the browser ever sees. `models`
-- caches the provider's /models answer from the last Test connection (empty: the author types a model id).
CREATE TABLE ai_integration (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    owner_id      uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    kind          text NOT NULL
                  CHECK (kind IN ('openai_compatible', 'openrouter', 'anthropic')),
    label         text NOT NULL,
    base_url      text NULL,
    credential    text NOT NULL,
    key_hint      text NOT NULL,
    default_model text NULL,
    models        text[] NOT NULL DEFAULT '{}',
    is_default    boolean NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    last_used_at  timestamptz NULL,
    last_error    text NULL,
    -- Only an OpenAI-compatible gateway takes a base URL; OpenRouter and Anthropic have fixed ones.
    CHECK ((kind = 'openai_compatible') = (base_url IS NOT NULL))
);
CREATE INDEX ai_integration_owner_idx ON ai_integration (owner_id);
CREATE UNIQUE INDEX ai_integration_one_default_idx ON ai_integration (owner_id) WHERE is_default;

-- Which account wrote a draft, next to the model it used.
ALTER TABLE ai_generation
    ADD COLUMN integration_id uuid REFERENCES ai_integration (id) ON DELETE SET NULL;
