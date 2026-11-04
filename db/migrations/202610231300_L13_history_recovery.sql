-- L13: version history and recovery.
--
-- prose_revision.source gains 'replaced' (R6): the text a stale save from another tab overwrote, kept so the author
-- can restore it from the scene's history.
ALTER TABLE prose_revision DROP CONSTRAINT prose_revision_source_check;
ALTER TABLE prose_revision ADD CONSTRAINT prose_revision_source_check
    CHECK (source IN ('import', 'manual', 'autosave', 'ai_apply', 'restore', 'agent', 'replaced'));

-- J15 step 9: a source use that comes back because the prose that holds its link was restored counts as new when the
-- source was signed off while the use was away, the same rule as restoring an archived scene
-- (scene_restore_reopens_signoff). prose_sync_embeds un-archives the use; this stamps it.
CREATE FUNCTION reference_use_restore_reopens_signoff() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM reference r WHERE r.id = NEW.reference_id AND r.signed_off_at > OLD.archived_at) THEN
        NEW.changed_at := now();
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER reference_use_restore_reopens_signoff
    BEFORE UPDATE OF archived_at ON reference_use
    FOR EACH ROW WHEN (OLD.archived_at IS NOT NULL AND NEW.archived_at IS NULL)
    EXECUTE FUNCTION reference_use_restore_reopens_signoff();
