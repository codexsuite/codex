-- #65: single-use links an agent on remote /mcp PUTs an image to (create_image_upload), so the bytes never pass through
-- the model. A link names one book (or series), the user who asked for it and the personal access token (#32) the asking
-- call ran under (NULL for the tokenless local user), and expires; it stops working with that token or user. Only a hash
-- of its secret is kept. used_at is stamped when an upload claims the link (and cleared again if that upload fails), asset_id once it lands.
CREATE TABLE asset_upload (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    token_hash  text NOT NULL UNIQUE,
    owner_id    uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    api_token_id uuid REFERENCES api_token (id) ON DELETE CASCADE,
    book_id     uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,  -- book or series
    filename    text NOT NULL,
    mime        text NOT NULL,
    bytes       bigint NOT NULL CHECK (bytes > 0),
    meta        jsonb NOT NULL DEFAULT '{}',
    created_at  timestamptz NOT NULL DEFAULT now(),
    expires_at  timestamptz NOT NULL,
    used_at     timestamptz,
    asset_id    uuid REFERENCES asset (id) ON DELETE SET NULL
);
