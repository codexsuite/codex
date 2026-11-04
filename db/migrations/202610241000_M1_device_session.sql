-- #35 M1: device sessions for the mobile app. A bearer sign-in that reaches /api and /hubs without cookies. The secret
-- (cdx_dev_<43 base62 chars>) is shown once; only its SHA-256 is kept. token_prefix is "cdx_dev_" plus the first 8
-- characters after it, so the Account page can tell devices apart. security_stamp is the user's stamp at sign-in: a
-- password change or an admin reset moves app_user's stamp on and ends the session, as it ends cookies. expires_at
-- slides 90 days on with use. Revoking sets revoked_at; nothing is deleted.
CREATE TABLE device_session (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    user_id        uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    name           text NOT NULL,
    token_prefix   text NOT NULL,
    token_hash     bytea NOT NULL UNIQUE,
    security_stamp uuid NOT NULL,
    expires_at     timestamptz NOT NULL,
    last_used_at   timestamptz NULL,
    revoked_at     timestamptz NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX device_session_active_idx ON device_session (user_id, created_at DESC) WHERE revoked_at IS NULL;
