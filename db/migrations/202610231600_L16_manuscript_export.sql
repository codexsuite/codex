-- L16.1 Manuscript export: Word, Markdown and EPUB copies of the manuscript are export_job rows too. They need no
-- layout template or trim, and a book runs one of each kind at a time. (The source export keeps its own rule in the app,
-- not an index: existing tests queue several source jobs.) Additive: existing rows and kinds are unchanged.
ALTER TABLE export_job DROP CONSTRAINT export_job_kind_check;
ALTER TABLE export_job ADD CONSTRAINT export_job_kind_check
    CHECK (kind IN ('proof', 'print', 'source', 'docx', 'markdown', 'epub'));

ALTER TABLE export_job DROP CONSTRAINT export_job_template_ck;
ALTER TABLE export_job ADD CONSTRAINT export_job_template_ck
    CHECK (kind IN ('source', 'docx', 'markdown', 'epub') OR (template_version_id IS NOT NULL AND trim_size IS NOT NULL));

CREATE UNIQUE INDEX export_job_live_document_uq ON export_job (book_id, kind)
    WHERE kind IN ('docx', 'markdown', 'epub') AND status IN ('queued', 'running');
