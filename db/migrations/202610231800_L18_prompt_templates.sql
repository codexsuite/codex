-- L18.1 (J22): the user's own prompt templates. The built-in templates stay files under prompts/ (served read-only by
-- GET /api/prompts); these rows are added to them per user. Models and keys are the user's AI integrations
-- (202610221600_ai_integration.sql, #36).

-- A user's prompt template. The body is the whole prompt: {{beat}}, {{context}}, {{style_guide}}, {{previous_prose}}
-- and {{length}} are filled in when a draft is generated.
CREATE TABLE prompt_template (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    user_id     uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    name        text NOT NULL CHECK (btrim(name) <> ''),
    description text NOT NULL DEFAULT '',
    body        text NOT NULL CHECK (btrim(body) <> ''),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX prompt_template_user_name_uq ON prompt_template (user_id, lower(name));
