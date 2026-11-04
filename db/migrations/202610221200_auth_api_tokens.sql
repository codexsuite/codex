-- #32 phase 1: personal access tokens for /mcp. A token acts as its user with its scopes. The secret
-- (cdx_pat_<43 base62 chars>) is shown once; only its SHA-256 is kept. token_prefix is "cdx_pat_" plus the first 8
-- characters after it, so the Account page can tell tokens apart. A revoked or expired token, or one whose user is
-- disabled, is refused.
CREATE TABLE api_token (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    user_id      uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    name         text NOT NULL,
    token_prefix text NOT NULL,
    token_hash   bytea NOT NULL UNIQUE,
    scopes       text[] NOT NULL,
    expires_at   timestamptz NULL,
    last_used_at timestamptz NULL,
    revoked_at   timestamptz NULL,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX api_token_user_idx ON api_token (user_id) WHERE revoked_at IS NULL;

-- Which token made a write over MCP, next to source = 'agent' and client_name. The name is copied, so History still
-- reads it after the token is revoked.
ALTER TABLE change_set
    ADD COLUMN api_token_id   uuid REFERENCES api_token (id) ON DELETE SET NULL,
    ADD COLUMN api_token_name text;
ALTER TABLE prose_revision
    ADD COLUMN api_token_id   uuid REFERENCES api_token (id) ON DELETE SET NULL,
    ADD COLUMN api_token_name text;
ALTER TABLE reference_event
    ADD COLUMN api_token_id   uuid REFERENCES api_token (id) ON DELETE SET NULL,
    ADD COLUMN api_token_name text;
ALTER TABLE ai_generation
    ADD COLUMN api_token_id   uuid REFERENCES api_token (id) ON DELETE SET NULL,
    ADD COLUMN api_token_name text;
