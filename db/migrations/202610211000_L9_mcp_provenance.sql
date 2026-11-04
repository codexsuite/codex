-- L9.2: provenance for writes made over MCP (#20 section 2.5, wave-3 decisions M11, M12, M14).
--
-- change_set: one row per MCP write (edit_outline batch, edit_prose, upsert_beat, resolve_draft, upsert_kb_entry,
-- link_kb_entry), structure and Knowledge base writes as well as prose. `undo` holds the steps that reverse it; the
-- undo tool runs them within 24 hours and stamps undone_at. `source` is who wrote it ('agent' for MCP; 'ui' is kept
-- for the UI's Undo, which can share the table later), `client_name` the MCP client's own name (clientInfo.name) and
-- `mcp_session` the session it came from.
CREATE TABLE change_set (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id      uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,
    source       text NOT NULL DEFAULT 'agent' CHECK (source IN ('agent', 'ui')),
    client_name  text,
    mcp_session  text,
    tool         text NOT NULL,
    summary      text NOT NULL,
    undo         jsonb NOT NULL,
    created_by   uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    undone_at    timestamptz
);
CREATE INDEX change_set_book_idx ON change_set (book_id, created_at DESC);

-- prose_revision.source gains 'agent': the revision cut before an agent's first edit to a scene in a session (M12:
-- one per scene per MCP session), holding the body as it was, so Restore is one click away.
ALTER TABLE prose_revision DROP CONSTRAINT prose_revision_source_check;
ALTER TABLE prose_revision ADD CONSTRAINT prose_revision_source_check
    CHECK (source IN ('import', 'manual', 'autosave', 'ai_apply', 'restore', 'agent'));
ALTER TABLE prose_revision
    ADD COLUMN client_name text,
    ADD COLUMN mcp_session text;
CREATE INDEX prose_revision_session_idx ON prose_revision (scene_id, mcp_session) WHERE mcp_session IS NOT NULL;

-- reference_event.source gains 'agent' (the References tools in L9.3 write it), with the client's name.
ALTER TABLE reference_event DROP CONSTRAINT reference_event_source_check;
ALTER TABLE reference_event ADD CONSTRAINT reference_event_source_check
    CHECK (source IN ('user', 'rule_check', 'link_check', 'scan', 'import', 'agent'));
ALTER TABLE reference_event ADD COLUMN client_name text;

-- ai_generation: how a draft was asked for ('ui' or 'mcp') and, over MCP, the client's name. A draft the agent wrote
-- itself (submit_draft) has model 'client': it names no model (M12), client_name says who wrote it.
ALTER TABLE ai_generation
    ADD COLUMN created_via text NOT NULL DEFAULT 'ui' CHECK (created_via IN ('ui', 'mcp')),
    ADD COLUMN client_name text;
