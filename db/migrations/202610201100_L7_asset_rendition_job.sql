-- L7.1: the queue behind asset renditions. One row per asset; the in-process worker claims queued rows (and rows
-- left 'running' by a stopped process) with FOR UPDATE SKIP LOCKED, writes the asset_rendition rows and marks the
-- job succeeded or failed. An asset with no row here was never queued (seed rows, non-image kinds).
CREATE TABLE asset_rendition_job (
    asset_id     uuid PRIMARY KEY REFERENCES asset (id) ON DELETE CASCADE,
    status       text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    attempts     integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    error        text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    started_at   timestamptz,
    finished_at  timestamptz
);
CREATE INDEX asset_rendition_job_open_idx ON asset_rendition_job (created_at) WHERE status IN ('queued', 'running');
