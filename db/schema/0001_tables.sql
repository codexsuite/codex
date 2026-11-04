-- Codex manuscript schema — proposal (rev 6)
-- Target: PostgreSQL 18 (built-in uuidv7(), regexp_count()).
--
-- Conventions
--   * snake_case, singular table names, uuid PKs (time-ordered v7)
--   * created_at / updated_at timestamptz on every mutable table
--   * archived_at = soft delete ("folded"/"removed" scenes stay recoverable)
--   * position = fractional-index sort key (text, COLLATE "C"); display
--     numbers ("Chapter 6", "Scene 2") are derived, never stored
--   * enums are text + CHECK so values can evolve without ALTER TYPE
--   * a series is a book row with kind = 'series'; anything scoped by
--     book_id (knowledge base, labels, style guide) can live on a single book or on
--     its series and be shared by every book in it
--   * all long text is Markdown with id-based links:
--       [Guyon](kb:<uuid>)  [Chapter 5](chapter:<uuid>)
--     kb_entry.slug and chapter.slug are the short handles the UI shows and
--     copies (e:goodhart, ch:shipping-small); stored text keeps the uuid.
--   * a source reference wraps the words it marks and points at a use row:
--       [Adding manpower…later.](ref:<reference_use uuid>)
--   * a scene's prose is ONE free-text Markdown document. Beats, knowledge
--     base additions and figures are objects embedded in it as directive lines
--     that point at their row; the row owns the object's fields, the document
--     owns where it sits:
--       ::beat{id="0199f1c2-…"}
--       ::kb-addition{id="0199f1c3-…"}
--       ::figure{id="0199f1c4-…"}
--     Print blocks that draw a knowledge base entry point at the entry, and
--     may repeat:
--       ::spell{kb="0199f1c5-…"}   ::statblock{kb="0199f1c5-…"}
--     Template-neutral layout directives carry no row:
--       ::pagebreak   :::readaloud … :::   :::sidebar{title="…"} … :::   :::aside … :::

-- ---------------------------------------------------------------------------
-- Helpers
-- ---------------------------------------------------------------------------

-- Words in a Markdown prose body, ignoring directive lines (::beat{…},
-- ::pagebreak, :::sidebar{…} and its closing :::), raw LaTeX blocks and link
-- targets. Words inside a container still count. Used by generated columns,
-- so it must stay IMMUTABLE.
CREATE FUNCTION markdown_word_count(body text) RETURNS integer
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT regexp_count(
        regexp_replace(
            regexp_replace(
                regexp_replace(body, '```\{=latex\}[\s\S]*?```', '', 'g'),          -- raw LaTeX
                '^:{2,}([a-z][a-z-]*(\{[^}\n]*\})?)?[ \t]*$', '', 'gn'),            -- directive lines
            '\]\([^)]*\)', ']', 'g'),                                               -- (kb:uuid) targets
        '[[:alnum:]][[:alnum:]''’.-]*')
$$;

-- Lower-case, hyphenated handle from a name: "Goodhart's Law" -> 'goodharts-law'.
CREATE FUNCTION slugify(name text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT coalesce(nullif(btrim(regexp_replace(
               regexp_replace(lower(name), '[''’]', '', 'g'),
               '[^a-z0-9]+', '-', 'g'), '-'), ''), 'untitled')
$$;

-- Replace every uuid in a text that is a key of map with its value. Used to
-- repoint kb:<uuid> links and ::beat / ::spell ids when rows are copied.
-- uuids are unique, so a plain replace can't hit anything else.
CREATE FUNCTION remap_uuids(body text, map jsonb) RETURNS text
LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE AS $$
DECLARE k text;
BEGIN
    IF body IS NULL OR map IS NULL OR map = '{}' THEN
        RETURN body;
    END IF;
    FOR k IN
        SELECT DISTINCT lower(m[1])
        FROM regexp_matches(body,
                 '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})', 'g') AS m
    LOOP
        IF map ? k THEN
            body := regexp_replace(body, k, map ->> k, 'gi');
        END IF;
    END LOOP;
    RETURN body;
END;
$$;

-- ---------------------------------------------------------------------------
-- Users
-- ---------------------------------------------------------------------------
CREATE TABLE app_user (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    email         text NOT NULL UNIQUE,
    display_name  text NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Book (kind = 'series' | 'book') -> Act -> Chapter -> Scene -> Prose ⊃ Beats
-- ---------------------------------------------------------------------------
CREATE TABLE book (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    kind               text NOT NULL DEFAULT 'book' CHECK (kind IN ('series', 'book')),
    owner_id           uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    series_id          uuid,                    -- NULL = standalone book (or this row is a series)
    series_kind        text GENERATED ALWAYS AS (
                           CASE WHEN series_id IS NOT NULL THEN 'series' END) STORED,
    series_number      integer,                 -- "Book 001"
    title              text NOT NULL,           -- not unique: copies share titles
    subtitle           text,
    description        text,
    author_name        text,                    -- name on the cover (pen name)
    cover_asset_id     uuid,                    -- FK added after asset; NULL = generated cover
    cover_palette      text,                    -- design token for the generated cover; NULL = picked from id
    status             text NOT NULL DEFAULT 'planning'
                       CHECK (status IN ('planning', 'draft', 'writing', 'revising', 'published')),
    target_word_count  integer CHECK (target_word_count > 0),
    style_guide        text,                    -- AI rules; a series' guide applies to all its books
    copied_from_id     uuid REFERENCES book (id) ON DELETE SET NULL,
    last_edited_at     timestamptz NOT NULL DEFAULT now(),  -- bumped by any save inside the book
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    archived_at        timestamptz,
    CONSTRAINT book_id_kind_uq UNIQUE (id, kind),            -- target for kind-checked FKs
    -- series_id may only point at a row whose kind = 'series'. A series
    -- can't be hard-deleted while it still has books (move or archive them).
    CONSTRAINT book_series_fk FOREIGN KEY (series_id, series_kind)
        REFERENCES book (id, kind),
    CONSTRAINT book_series_not_nested CHECK (kind = 'book' OR series_id IS NULL),
    -- a book in a series always has its number; archived books keep theirs
    CONSTRAINT book_series_number_needs_series
        CHECK ((series_number IS NULL) = (series_id IS NULL)),
    CONSTRAINT book_series_number_positive CHECK (series_number > 0),
    CONSTRAINT book_series_number_uq
        UNIQUE (series_id, series_number) DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX book_owner_idx ON book (owner_id, last_edited_at DESC);
CREATE INDEX book_series_idx ON book (series_id);

-- ---------------------------------------------------------------------------
-- Assets (#5): uploaded files. Images belong to a book or a series (shared
-- like the knowledge base); template packages and export files can belong to
-- the user alone. Never hard-deleted while used: "deleted from the asset
-- library" sets archived_at, which Layout's preflight reports as a missing
-- asset, so FKs to asset are NO ACTION.
-- ---------------------------------------------------------------------------
CREATE TABLE asset (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    owner_id        uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    book_id         uuid REFERENCES book (id) ON DELETE CASCADE,  -- book or series; NULL = the user's
    kind            text NOT NULL CHECK (kind IN ('image', 'document', 'archive', 'font', 'other')),
    filename        text NOT NULL,
    mime            text NOT NULL,
    bytes           bigint NOT NULL CHECK (bytes >= 0),
    width_px        integer CHECK (width_px > 0),   -- images
    height_px       integer CHECK (height_px > 0),
    storage_key     text NOT NULL,
    meta            jsonb NOT NULL DEFAULT '{}',    -- prompt, source, licence notes...
    copied_from_id  uuid REFERENCES asset (id) ON DELETE SET NULL,  -- series copy-down
    created_at      timestamptz NOT NULL DEFAULT now(),
    archived_at     timestamptz,
    CONSTRAINT asset_image_size_ck
        CHECK (kind <> 'image' OR (width_px IS NOT NULL AND height_px IS NOT NULL))
);
CREATE INDEX asset_book_idx ON asset (book_id) WHERE book_id IS NOT NULL;

-- Derived files, made in the background: web (WebP ~1600 px) for Quick
-- preview, proxy (150 dpi JPEG) for proofs, print (300 dpi at the largest
-- placed size) for Export for printer.
CREATE TABLE asset_rendition (
    asset_id     uuid NOT NULL REFERENCES asset (id) ON DELETE CASCADE,
    purpose      text NOT NULL CHECK (purpose IN ('web', 'proxy', 'print')),
    mime         text NOT NULL,
    width_px     integer NOT NULL CHECK (width_px > 0),
    height_px    integer NOT NULL CHECK (height_px > 0),
    bytes        bigint NOT NULL CHECK (bytes >= 0),
    storage_key  text NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (asset_id, purpose)
);

ALTER TABLE book ADD CONSTRAINT book_cover_asset_fk
    FOREIGN KEY (cover_asset_id) REFERENCES asset (id);

CREATE TABLE act (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id      uuid NOT NULL,
    book_kind    text NOT NULL GENERATED ALWAYS AS ('book') STORED,
    position     text COLLATE "C" NOT NULL,
    title        text NOT NULL,                 -- '' until named ("Name this act")
    summary      text,
    opener_asset_id  uuid REFERENCES asset (id),  -- part-opener art; the template draws the page
    opener_caption   text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    archived_at  timestamptz,
    -- acts hang off real books only, never a series row
    CONSTRAINT act_book_fk FOREIGN KEY (book_id, book_kind)
        REFERENCES book (id, kind) ON DELETE CASCADE,
    CONSTRAINT act_id_book_uq UNIQUE (id, book_id),  -- target for composite FKs
    -- live rows only: an archived act keeps its key for Restore, and a new
    -- act may take it meanwhile (Restore then picks a fresh key)
    CONSTRAINT act_position_uq EXCLUDE USING btree (book_id WITH =, position WITH =)
        WHERE (archived_at IS NULL) DEFERRABLE INITIALLY DEFERRED
);

CREATE TABLE chapter (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id      uuid NOT NULL,
    act_id       uuid NOT NULL,
    position     text COLLATE "C" NOT NULL,
    title        text NOT NULL,
    slug         text NOT NULL,                 -- "Copy reference" ch:<slug>; set from the title, never changes
    summary      text,                          -- "Shape of the chapter"
    notes        text,                          -- outline decisions, folded list, etc.
    is_numbered  boolean NOT NULL DEFAULT true, -- false for Preface / front matter
    opener_asset_id  uuid REFERENCES asset (id),  -- chapter-opener art
    opener_caption   text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    archived_at  timestamptz,                   -- "Delete chapter" archives, so archived scenes in it survive
    -- composite FK: a chapter can move between acts, but only within its book
    CONSTRAINT chapter_act_fk FOREIGN KEY (act_id, book_id)
        REFERENCES act (id, book_id) ON DELETE CASCADE,
    CONSTRAINT chapter_id_book_uq UNIQUE (id, book_id),
    CONSTRAINT chapter_slug_uq UNIQUE (book_id, slug),
    CONSTRAINT chapter_position_uq EXCLUDE USING btree (act_id WITH =, position WITH =)
        WHERE (archived_at IS NULL) DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX chapter_book_idx ON chapter (book_id);

CREATE TABLE scene (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id      uuid NOT NULL,
    chapter_id   uuid NOT NULL,
    position     text COLLATE "C" NOT NULL,
    title        text,                          -- gutter subtitle, e.g. "Performance Optimization"
    summary      text,                          -- gutter / Plan-board card text
    plan_notes   text,                          -- purpose / covers / boundaries / sources
    status       text NOT NULL DEFAULT 'outline'
                 CHECK (status IN ('empty', 'outline', 'beats', 'draft', 'revised', 'final')),
    import_ref   text,                          -- legacy id, e.g. 'ch08-s02'
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    archived_at  timestamptz,                   -- folded / removed scenes
    CONSTRAINT scene_chapter_fk FOREIGN KEY (chapter_id, book_id)
        REFERENCES chapter (id, book_id) ON DELETE CASCADE,
    CONSTRAINT scene_id_book_uq UNIQUE (id, book_id),  -- target for composite FKs
    CONSTRAINT scene_position_uq EXCLUDE USING btree (chapter_id WITH =, position WITH =)
        WHERE (archived_at IS NULL) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT scene_import_ref_uq UNIQUE (book_id, import_ref)
);
CREATE INDEX scene_book_idx ON scene (book_id);

-- The scene's prose: one free-text Markdown document with embedded
-- ::beat{id="…"} / ::kb-addition{id="…"} / ::figure{id="…"} lines.
CREATE TABLE prose (
    scene_id    uuid PRIMARY KEY REFERENCES scene (id) ON DELETE CASCADE,
    body        text NOT NULL DEFAULT '',
    word_count  integer GENERATED ALWAYS AS (markdown_word_count(body)) STORED,
    version     integer NOT NULL DEFAULT 1,     -- optimistic concurrency for websocket autosave
    updated_by  uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- A beat is an object inside the scene's prose. This row owns the beat's
-- fields; its ::beat line in prose.body owns where it sits. Removing the
-- line archives the row (prose_sync_embeds), keeping generation history and
-- context links; undo restores it. Beat edits are not versioned.
CREATE TABLE beat (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    scene_id           uuid NOT NULL REFERENCES scene (id) ON DELETE CASCADE,
    body               text NOT NULL DEFAULT '',  -- instruction for the writer / model
    target_word_count  integer CHECK (target_word_count > 0),  -- 200 / 400 / 600 / custom
    is_hidden          boolean NOT NULL DEFAULT false,
    model              text,                    -- model id chosen in "Set up AI"
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    archived_at        timestamptz
);
CREATE INDEX beat_scene_idx ON beat (scene_id);

-- An image placed in the prose as ::figure{id="…"}. Like a beat, the row owns
-- the fields and the line owns the place, so changing placement in Layout
-- never writes prose.body. Removing the line archives the row.
CREATE TABLE figure (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    scene_id     uuid NOT NULL REFERENCES scene (id) ON DELETE CASCADE,
    asset_id     uuid NOT NULL REFERENCES asset (id),  -- book or series asset (figure_scope)
    placement    text NOT NULL DEFAULT 'column'
                 CHECK (placement IN ('column', 'page', 'spread', 'float-top')),
    caption      text,
    alt_text     text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    archived_at  timestamptz
);
CREATE INDEX figure_scene_idx ON figure (scene_id);
CREATE INDEX figure_asset_idx ON figure (asset_id);

-- Every AI call: beats-for-a-scene, prose-for-a-beat, continue-writing...
-- Applying one inserts its output as plain prose below the ::beat line and
-- cuts a prose_revision pointing back here.
CREATE TABLE ai_generation (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id        uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,
    scene_id       uuid REFERENCES scene (id) ON DELETE CASCADE,
    beat_id        uuid REFERENCES beat (id) ON DELETE SET NULL,
    kind           text NOT NULL
                   CHECK (kind IN ('scene_beats', 'beat_prose', 'continue', 'other')),
    model          text NOT NULL,
    request        jsonb NOT NULL,              -- resolved prompt + context snapshot (kb entry ids, prior prose)
    output         text,
    status         text NOT NULL DEFAULT 'pending'
                   CHECK (status IN ('streaming', 'pending', 'applied', 'discarded', 'failed')),
    retry_of_id    uuid REFERENCES ai_generation (id) ON DELETE SET NULL,
    input_tokens   integer,
    output_tokens  integer,
    created_by     uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    resolved_at    timestamptz                  -- applied / discarded
);
CREATE INDEX ai_generation_beat_idx ON ai_generation (beat_id, created_at DESC);
CREATE INDEX ai_generation_scene_idx ON ai_generation (scene_id, created_at DESC);

-- Append-only snapshots of a scene's prose. Autosave writes prose.body; a
-- revision is cut on AI apply, restore, explicit "save version", or idle gap.
CREATE TABLE prose_revision (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    scene_id       uuid NOT NULL REFERENCES prose (scene_id) ON DELETE CASCADE,
    body           text NOT NULL,
    word_count     integer GENERATED ALWAYS AS (markdown_word_count(body)) STORED,
    source         text NOT NULL
                   CHECK (source IN ('import', 'manual', 'autosave', 'ai_apply', 'restore')),
    generation_id  uuid REFERENCES ai_generation (id) ON DELETE SET NULL,
    created_by     uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX prose_revision_scene_idx ON prose_revision (scene_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Knowledge base (#3) — a category belongs to a book or a series; entries inherit
-- that scope. A book sees its own categories plus its series' categories.
-- ---------------------------------------------------------------------------
CREATE TABLE kb_category (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id     uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,  -- book or series
    name        text NOT NULL,                  -- "Story's with Moral"
    tag         text,                           -- short tag shown on entries, e.g. "story"
    color       text,                           -- design token name, e.g. 'category-amber'
    icon        text,
    position    text COLLATE "C" NOT NULL,
    -- the Details fields every entry in this category shows, in order:
    -- [{"key":"born","label":"Born"}, ...]; values live in kb_entry.fields
    field_defs  jsonb NOT NULL DEFAULT '[]',
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT kb_category_id_book_uq UNIQUE (id, book_id),
    CONSTRAINT kb_category_name_uq UNIQUE (book_id, name),
    CONSTRAINT kb_category_field_defs_is_array CHECK (jsonb_typeof(field_defs) = 'array')
);

CREATE TABLE kb_entry (
    id                         uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id                    uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,  -- = category's scope
    category_id                uuid NOT NULL,
    name                       text NOT NULL,
    slug                       text NOT NULL,   -- e:<slug>, set from the first name, never changes
    position                   text COLLATE "C" NOT NULL,  -- order in its category ("5 of 12"); ties by id
    aliases                    text[] NOT NULL DEFAULT '{}',
    tags                       text[] NOT NULL DEFAULT '{}',
    summary                    text,            -- sidebar excerpt; also sent as AI context
    body                       text NOT NULL DEFAULT '',
    fields                     jsonb NOT NULL DEFAULT '{}',  -- values for kb_category.field_defs keys
    custom_fields              jsonb NOT NULL DEFAULT '[]',  -- fields the author adds, in order: [{"label":"URL","value":"…"}]
    always_in_context          boolean NOT NULL DEFAULT false,
    exclude_from_auto_context  boolean NOT NULL DEFAULT false,
    import_url                 text,            -- "Import from URL"
    import_archive_url         text,            -- archived copy of that page, as for a web source
    imported_at                timestamptz,
    copied_from_id             uuid REFERENCES kb_entry (id) ON DELETE SET NULL,  -- series copy-down
    external_id                text,            -- Novelcrafter KSUID on import
    created_at                 timestamptz NOT NULL DEFAULT now(),
    updated_at                 timestamptz NOT NULL DEFAULT now(),
    archived_at                timestamptz,     -- Delete; Undo clears it
    -- ON UPDATE CASCADE: promoting a category from a book to its series is a
    -- one-row UPDATE and its entries follow. Deleting a non-empty category is blocked.
    CONSTRAINT kb_entry_category_fk FOREIGN KEY (category_id, book_id)
        REFERENCES kb_category (id, book_id) ON UPDATE CASCADE,
    -- per scope row; a book's own entry wins over a same-slug series entry
    CONSTRAINT kb_entry_slug_uq UNIQUE (book_id, slug),
    CONSTRAINT kb_entry_fields_is_object CHECK (jsonb_typeof(fields) = 'object'),
    CONSTRAINT kb_entry_custom_fields_is_array CHECK (jsonb_typeof(custom_fields) = 'array')
);
CREATE INDEX kb_entry_book_idx ON kb_entry (book_id);
CREATE INDEX kb_entry_category_idx ON kb_entry (category_id, position);
CREATE INDEX kb_entry_aliases_idx ON kb_entry USING gin (aliases);

-- "Related": entries linked both ways. One row per pair, smaller id first.
CREATE TABLE kb_entry_link (
    entry_a_id  uuid NOT NULL REFERENCES kb_entry (id) ON DELETE CASCADE,
    entry_b_id  uuid NOT NULL REFERENCES kb_entry (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (entry_a_id, entry_b_id),
    CONSTRAINT kb_entry_link_ordered CHECK (entry_a_id < entry_b_id)
);
CREATE INDEX kb_entry_link_b_idx ON kb_entry_link (entry_b_id);

-- "+ Knowledge base" on a scene (gutter / Plan card)
CREATE TABLE scene_kb (
    scene_id        uuid NOT NULL REFERENCES scene (id) ON DELETE CASCADE,
    kb_entry_id     uuid NOT NULL REFERENCES kb_entry (id) ON DELETE CASCADE,
    PRIMARY KEY (scene_id, kb_entry_id)
);
CREATE INDEX scene_kb_entry_idx ON scene_kb (kb_entry_id);

-- "+ Context" on a beat: extra entries fed to the model for this beat
CREATE TABLE beat_kb (
    beat_id         uuid NOT NULL REFERENCES beat (id) ON DELETE CASCADE,
    kb_entry_id     uuid NOT NULL REFERENCES kb_entry (id) ON DELETE CASCADE,
    PRIMARY KEY (beat_id, kb_entry_id)
);
CREATE INDEX beat_kb_entry_idx ON beat_kb (kb_entry_id);

-- "Knowledge base addition": what we learn about an entry at this point in the story.
-- Embedded in prose.body as ::kb-addition{id="…"}.
CREATE TABLE kb_progression (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    kb_entry_id     uuid NOT NULL REFERENCES kb_entry (id) ON DELETE CASCADE,
    scene_id        uuid NOT NULL REFERENCES scene (id) ON DELETE CASCADE,
    body            text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    archived_at     timestamptz
);
CREATE INDEX kb_progression_scene_idx ON kb_progression (scene_id);
CREATE INDEX kb_progression_entry_idx ON kb_progression (kb_entry_id);

-- ---------------------------------------------------------------------------
-- Labels — book- or series-level, same scoping as the knowledge base
-- ---------------------------------------------------------------------------
CREATE TABLE label (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id     uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,  -- book or series
    name        text NOT NULL,
    color       text,
    position    text COLLATE "C" NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT label_name_uq UNIQUE (book_id, name)
);

CREATE TABLE scene_label (
    scene_id  uuid NOT NULL REFERENCES scene (id) ON DELETE CASCADE,
    label_id  uuid NOT NULL REFERENCES label (id) ON DELETE CASCADE,
    PRIMARY KEY (scene_id, label_id)
);
CREATE INDEX scene_label_label_idx ON scene_label (label_id);

-- ---------------------------------------------------------------------------
-- References (#2) — sources, where the book uses them, copyright clearance.
-- Book-level only: uses, permissions and sign-off are for one book's print
-- run, so a source used in two books of a series is two rows.
-- ---------------------------------------------------------------------------

-- Reference data shipped with the app.
CREATE TABLE licence (
    code                     text PRIMARY KEY,
    label                    text NOT NULL,
    allows_reuse             boolean NOT NULL,  -- figures and extracts may be reproduced with credit
    needs_fair_use_decision  boolean NOT NULL DEFAULT false,  -- a partial quote needs a recorded decision
    note                     text
);
INSERT INTO licence (code, label, allows_reuse, needs_fair_use_decision, note) VALUES
    ('all_rights_reserved', 'All rights reserved', false, false, NULL),
    ('copy_in_full',        'Copy in full only',   false, true,  'The notice allows copying the whole text, not extracts'),
    ('cc_by_4',             'CC BY 4.0',           true,  false, NULL),
    ('cc_by_nc_4',          'CC BY-NC 4.0',        false, true,  'Non-commercial only; a sold book needs permission'),
    ('public_domain',       'Public domain',       true,  false, NULL),
    ('unknown',             'Not recorded',        false, false, NULL);

CREATE TABLE reference_kind (
    code             text PRIMARY KEY,
    label            text NOT NULL,
    csl_type         text NOT NULL,     -- CSL item type for citeproc and export
    required_fields  text[] NOT NULL    -- reference columns; an empty one is "Citation incomplete"
);
INSERT INTO reference_kind (code, label, csl_type, required_fields) VALUES
    ('book',   'Book',     'book',            '{authors,title,year,publisher}'),
    ('paper',  'Paper',    'article-journal', '{authors,title,year,publisher}'),
    ('web',    'Web page', 'webpage',         '{title,url,accessed_on}'),
    ('report', 'Report',   'report',          '{authors,title,year,url,accessed_on}'),
    ('song',   'Song',     'song',            '{authors,title,year}');

-- Book settings shared by References and print (#13). A row on a series is
-- the default for its books; a book's own non-null values win
-- (book_publication_settings). Lyrics/poetry always need permission and
-- figures need it unless licensed; those two rules are fixed.
-- NULL = inherit ("Series"); clearing "This book ×" writes NULL. To override a
-- series value with "nothing", store '' (publisher: "you publish it yourself")
-- or '{}' (formats). Blank or 0 for print run means inherit.
CREATE TABLE publication_settings (
    book_id           uuid PRIMARY KEY REFERENCES book (id) ON DELETE CASCADE,  -- book or series
    citation_style    text CHECK (citation_style IN ('chicago', 'apa', 'harvard')),
    rules_name        text,             -- "Default", or the publisher guide the limits come from
    quote_word_limit  integer CHECK (quote_word_limit >= 0),  -- prose quoted from one source without permission
    publisher_name    text,             -- the permission letter: who is publishing ('' = self) ...
    planned_year      integer CHECK (planned_year BETWEEN 1900 AND 2200),  -- ... when ...
    print_run         integer CHECK (print_run > 0),  -- ... how many ...
    formats           text[] CHECK (formats <@ ARRAY['print', 'ebook', 'pdf', 'audio']),  -- ... in which formats ...
    territory         text,             -- ... and where: 'World English'
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE reference (
    id                    uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id               uuid NOT NULL,
    book_kind             text NOT NULL GENERATED ALWAYS AS ('book') STORED,
    kind                  text NOT NULL REFERENCES reference_kind (code),
    title                 text NOT NULL DEFAULT '',
    authors               jsonb NOT NULL DEFAULT '[]',  -- CSL names: [{"family":"Beck","given":"Kent"}] or [{"literal":"DORA"}]
    year                  integer,
    publisher             text,         -- publisher, site name or record label
    container_title       text,         -- journal or periodical, e.g. 'Datamation'
    volume                text,
    issue                 text,
    page_range            text,         -- the source's own pages, e.g. '28–31'
    edition               text,
    isbn                  text,         -- normalised ISBN-13
    doi                   text,
    url                   text,
    accessed_on           date,
    licence               text NOT NULL DEFAULT 'unknown' REFERENCES licence (code),
    rights_holder         text,
    permissions_contact   text,         -- where a permission request goes
    fair_use_decided_at   timestamptz,  -- "Accept as fair use"
    fair_use_decided_by   uuid REFERENCES app_user (id) ON DELETE SET NULL,
    fair_use_note         text,
    signed_off_at         timestamptz,  -- "Mark as cleared"; a later use or change reopens it
    signed_off_by         uuid REFERENCES app_user (id) ON DELETE SET NULL,
    kb_entry_id           uuid REFERENCES kb_entry (id) ON DELETE SET NULL,  -- "Codex" link, in book scope
    cite_key              text,         -- BibTeX key, e.g. 'beck1999'
    csl_extra             jsonb NOT NULL DEFAULT '{}',  -- imported CSL variables with no column
    external_provider     text CHECK (external_provider IN ('zotero', 'bibtex', 'ris')),
    external_key          text,         -- Zotero item key / BibTeX key on import
    merged_into_id        uuid REFERENCES reference (id) ON DELETE SET NULL,  -- "Merge a duplicate"
    created_by            uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now(),
    archived_at           timestamptz,
    -- references hang off real books only, never a series row
    CONSTRAINT reference_book_fk FOREIGN KEY (book_id, book_kind)
        REFERENCES book (id, kind) ON DELETE CASCADE,
    CONSTRAINT reference_id_book_uq UNIQUE (id, book_id),
    CONSTRAINT reference_cite_key_uq UNIQUE (book_id, cite_key),
    CONSTRAINT reference_external_uq UNIQUE (book_id, external_provider, external_key),
    CONSTRAINT reference_authors_is_array CHECK (jsonb_typeof(authors) = 'array')
);
-- duplicate matching on add / import
CREATE INDEX reference_isbn_idx ON reference (book_id, isbn) WHERE isbn IS NOT NULL;
CREATE INDEX reference_doi_idx ON reference (book_id, lower(doi)) WHERE doi IS NOT NULL;
CREATE INDEX reference_url_idx ON reference (book_id, url) WHERE url IS NOT NULL;
CREATE INDEX reference_kb_entry_idx ON reference (kb_entry_id) WHERE kb_entry_id IS NOT NULL;

-- One use of a source: a passage in a scene's prose wrapped as a link,
--   [Adding manpower to a late software project makes it later.](ref:<use id>)
-- The link marks the words; this row says how they use the source. A quote
-- that spans paragraphs repeats the same id on each paragraph. Removing every
-- link archives the row (prose_sync_embeds); undo restores it.
CREATE TABLE reference_use (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id       uuid NOT NULL,
    reference_id  uuid NOT NULL,
    scene_id      uuid NOT NULL,
    kind          text NOT NULL DEFAULT 'quote'
                  CHECK (kind IN ('quote', 'paraphrase', 'data', 'figure', 'lyric')),
    locator       text,                 -- where in the source: 'pp. 41–43', 'Table 5.1'
    excerpt       text NOT NULL DEFAULT '',  -- the linked words, kept in step with the prose
    word_count    integer GENERATED ALWAYS AS (markdown_word_count(excerpt)) STORED,
    asset_id      uuid REFERENCES asset (id),  -- the reproduced figure
    -- set while the link exists only in a pending AI draft: it counts toward
    -- marker numbers and clearance, and prose saves don't archive it. Apply
    -- clears it; Discard archives the use (ai_generation_resolve_uses).
    generation_id uuid REFERENCES ai_generation (id) ON DELETE CASCADE,
    changed_at    timestamptz NOT NULL DEFAULT now(),  -- added, re-typed or lengthened; reopens a signed-off source
    created_by    uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    archived_at   timestamptz,          -- its links were removed from the prose
    -- composite FKs: the source and the scene must be in the same book
    CONSTRAINT reference_use_reference_fk FOREIGN KEY (reference_id, book_id)
        REFERENCES reference (id, book_id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT reference_use_scene_fk FOREIGN KEY (scene_id, book_id)
        REFERENCES scene (id, book_id) ON DELETE CASCADE
);
CREATE INDEX reference_use_reference_idx ON reference_use (reference_id);
CREATE INDEX reference_use_scene_idx ON reference_use (scene_id);

-- "Draft request" → "Mark as sent" → "Record grant" / "Record refusal".
-- "Ask again" after a refusal is a new row.
CREATE TABLE permission_request (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    reference_id      uuid NOT NULL REFERENCES reference (id) ON DELETE CASCADE,
    status            text NOT NULL DEFAULT 'draft'
                      CHECK (status IN ('draft', 'sent', 'granted', 'refused', 'withdrawn')),
    to_address        text NOT NULL,
    letter            text NOT NULL,    -- Markdown, drafted from the uses and publication_settings
    sent_at           timestamptz,
    remind_at         timestamptz,      -- "Reminder due in 3 days"
    last_reminded_at  timestamptz,
    responded_at      timestamptz,
    fee_amount        numeric(10, 2),
    fee_currency      char(3),
    credit_line       text,             -- printed with the bibliography once granted
    terms             text,             -- conditions attached to the grant
    created_by        uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT permission_request_sent_ck
        CHECK (status IN ('draft', 'withdrawn') OR sent_at IS NOT NULL),
    CONSTRAINT permission_request_answered_ck
        CHECK (status NOT IN ('granted', 'refused') OR responded_at IS NOT NULL)
);
-- at most one live request per source
CREATE UNIQUE INDEX permission_request_live_uq ON permission_request (reference_id)
    WHERE status IN ('draft', 'sent', 'granted');

-- Latest ISBN / DOI / URL check. Only counts while checked_value still
-- matches the field, so editing the field clears a stale result.
CREATE TABLE reference_check (
    reference_id   uuid NOT NULL REFERENCES reference (id) ON DELETE CASCADE,
    field          text NOT NULL CHECK (field IN ('isbn', 'doi', 'url')),
    checked_value  text NOT NULL,
    status         text NOT NULL CHECK (status IN ('checking', 'ok', 'redirect', 'not_found', 'error')),
    http_status    integer,
    resolved_url   text,                -- where a redirect lands ("Update it")
    provider       text,                -- 'Open Library', 'Crossref', 'http'
    checked_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (reference_id, field)
);

-- Files kept with a source: the archived web page, the source text the
-- unflagged-quote scan compares against, the signed permission letter.
CREATE TABLE reference_attachment (
    id                     uuid PRIMARY KEY DEFAULT uuidv7(),
    reference_id           uuid NOT NULL REFERENCES reference (id) ON DELETE CASCADE,
    permission_request_id  uuid REFERENCES permission_request (id) ON DELETE CASCADE,
    kind                   text NOT NULL
                           CHECK (kind IN ('web_archive', 'source_text', 'permission_letter', 'other')),
    asset_id               uuid REFERENCES asset (id),
    archive_url            text,        -- e.g. a Wayback Machine snapshot
    extracted_text         text,        -- plain text for matching (source_text)
    created_by             uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at             timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reference_attachment_letter_ck
        CHECK (kind <> 'permission_letter' OR permission_request_id IS NOT NULL)
);
CREATE INDEX reference_attachment_reference_idx ON reference_attachment (reference_id);

-- The History list. Written by the app; NULL actor = Codex itself
-- ("Rule check", "Link check", the manuscript scan, an import).
CREATE TABLE reference_event (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    reference_id  uuid NOT NULL REFERENCES reference (id) ON DELETE CASCADE,
    actor_id      uuid REFERENCES app_user (id) ON DELETE SET NULL,
    source        text NOT NULL DEFAULT 'user'
                  CHECK (source IN ('user', 'rule_check', 'link_check', 'scan', 'import')),
    action        text NOT NULL,        -- 'added', 'signed_off', 'licence_set', 'permission_sent', ...
    detail        jsonb NOT NULL DEFAULT '{}',
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX reference_event_reference_idx ON reference_event (reference_id, created_at DESC);

-- "Possible unflagged quotes": the scan on save finds quoted text with no
-- marker, or text close to a source's attached text. A dismissed fingerprint
-- is never raised again for the book.
CREATE TABLE quote_flag (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id          uuid NOT NULL,
    scene_id         uuid NOT NULL,
    excerpt          text NOT NULL,
    fingerprint      text GENERATED ALWAYS AS (
                         md5(lower(btrim(regexp_replace(excerpt, '[^[:alnum:]]+', ' ', 'g'))))) STORED,
    reason           text NOT NULL CHECK (reason IN ('quotation_marks', 'source_match')),
    reference_id     uuid REFERENCES reference (id) ON DELETE SET NULL,  -- best guess: "Cite Whitlock, 2021"
    match_score      real CHECK (match_score BETWEEN 0 AND 1),           -- "93% match"
    locator          text,                                               -- "p. 112"
    status           text NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'cited', 'dismissed')),
    resolved_use_id  uuid REFERENCES reference_use (id) ON DELETE SET NULL,
    resolved_by      uuid REFERENCES app_user (id) ON DELETE SET NULL,
    resolved_at      timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT quote_flag_scene_fk FOREIGN KEY (scene_id, book_id)
        REFERENCES scene (id, book_id) ON DELETE CASCADE,
    CONSTRAINT quote_flag_fingerprint_uq UNIQUE (book_id, fingerprint)
);
CREATE INDEX quote_flag_open_idx ON quote_flag (book_id) WHERE status = 'open';

-- ---------------------------------------------------------------------------
-- Layout and print (#13): templates, the book's choice, export jobs
-- ---------------------------------------------------------------------------

-- A layout template: built in (owner_id NULL) or the user's own fork/upload.
CREATE TABLE layout_template (
    id                      uuid PRIMARY KEY DEFAULT uuidv7(),
    owner_id                uuid REFERENCES app_user (id) ON DELETE CASCADE,  -- NULL = built in
    slug                    text NOT NULL,      -- 'fantasy-rpg', 'novel-6x9', 'manuscript'
    name                    text NOT NULL,      -- "Fantasy RPG (5e style)"
    short_name              text,               -- toolbar label
    description             text,
    thumbnail_asset_id      uuid REFERENCES asset (id),  -- rendered from sample.md
    forked_from_version_id  uuid,               -- "forked from Fantasy RPG v1.4.0" (FK below)
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    archived_at             timestamptz,
    CONSTRAINT layout_template_slug_uq UNIQUE NULLS NOT DISTINCT (owner_id, slug)
);

-- An immutable package version. Upload or Edit files makes a new one; the
-- newest published version is the "v1.5.0 available" the book can upgrade to.
-- manifest is template.yaml: trims, options schema and defaults, page
-- margins, directive -> rendering map, and files keyed by path:
-- {"trims": ["6x9", "a4"], "files": {"preview.css": {"description": "…"}}, ...}
CREATE TABLE layout_template_version (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    template_id       uuid NOT NULL REFERENCES layout_template (id) ON DELETE CASCADE,
    major             integer NOT NULL CHECK (major >= 0),
    minor             integer NOT NULL CHECK (minor >= 0),
    patch             integer NOT NULL CHECK (patch >= 0),
    version           text GENERATED ALWAYS AS (major || '.' || minor || '.' || patch) STORED,
    engine            text NOT NULL CHECK (engine IN ('pdflatex', 'xelatex', 'lualatex', 'tectonic')),
    manifest          jsonb NOT NULL,
    has_preview_css   boolean GENERATED ALWAYS AS (coalesce(manifest -> 'files' ? 'preview.css', false)) STORED,
    changelog         text[] NOT NULL DEFAULT '{}',  -- "What changes" in the upgrade preview
    package_asset_id  uuid REFERENCES asset (id),
    published_at      timestamptz,                   -- NULL = draft, not offered to books
    created_by        uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT layout_template_version_uq UNIQUE (template_id, major, minor, patch),
    CONSTRAINT layout_template_version_id_template_uq UNIQUE (id, template_id),
    CONSTRAINT layout_template_version_trims_ck
        CHECK (jsonb_typeof(manifest -> 'trims') = 'array')
);

ALTER TABLE layout_template ADD CONSTRAINT layout_template_forked_from_fk
    FOREIGN KEY (forked_from_version_id) REFERENCES layout_template_version (id) ON DELETE SET NULL;

-- The book's settings for each template it has used, so switching back
-- restores the pinned version, trim and options. Bleed, crop marks and image
-- quality are chosen per export (export_job.settings), not stored here.
CREATE TABLE book_layout_template (
    book_id              uuid NOT NULL,
    book_kind            text NOT NULL GENERATED ALWAYS AS ('book') STORED,
    template_id          uuid NOT NULL REFERENCES layout_template (id),
    template_version_id  uuid NOT NULL,         -- pinned; upgrading is an explicit Apply
    trim_size            text NOT NULL,         -- one of the version's manifest trims
    options              jsonb NOT NULL DEFAULT '{}',  -- values for the manifest's options schema
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (book_id, template_id),
    CONSTRAINT book_layout_template_book_fk FOREIGN KEY (book_id, book_kind)
        REFERENCES book (id, kind) ON DELETE CASCADE,
    CONSTRAINT book_layout_template_version_fk FOREIGN KEY (template_version_id, template_id)
        REFERENCES layout_template_version (id, template_id)
);

-- The template the book uses now.
CREATE TABLE book_layout (
    book_id      uuid PRIMARY KEY,
    template_id  uuid NOT NULL,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT book_layout_template_fk FOREIGN KEY (book_id, template_id)
        REFERENCES book_layout_template (book_id, template_id)
);

-- Background work that must survive a reload: chapter proofs, Export for
-- printer, and Export source (.zip). A succeeded proof is the proof cache:
-- it is reused while cache_key matches, and older rows back "Show the old
-- proof". content_hash covers the chapter's prose in order, the figures and
-- entries it embeds, their renditions, and the chapter title and opener.
CREATE TABLE export_job (
    id                   uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id              uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,
    kind                 text NOT NULL CHECK (kind IN ('proof', 'print', 'source')),
    chapter_id           uuid REFERENCES chapter (id) ON DELETE CASCADE,  -- proofs are one chapter
    template_version_id  uuid REFERENCES layout_template_version (id),
    trim_size            text,
    options              jsonb NOT NULL DEFAULT '{}',
    settings             jsonb NOT NULL DEFAULT '{}',  -- bleed, crop_marks, dpi (300|150), draft watermark, format
    content_hash         text,
    cache_key            text GENERATED ALWAYS AS (
                             md5(coalesce(content_hash, '') || '|' || coalesce(template_version_id::text, '')
                                 || '|' || coalesce(trim_size, '') || '|' || options::text)) STORED,
    status               text NOT NULL DEFAULT 'queued'
                         CHECK (status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')),
    step                 text,                 -- "Assembling", "Rendering images", "Typesetting (pass 2)"
    progress             real CHECK (progress BETWEEN 0 AND 1),
    preflight            jsonb,                -- snapshot taken when queued; live preflight is computed
    diagnostics          jsonb,                -- overfull lines etc. from the log
    log                  text,
    error                text,
    page_count           integer,
    output_asset_id      uuid REFERENCES asset (id),
    created_by           uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    started_at           timestamptz,
    finished_at          timestamptz,
    CONSTRAINT export_job_proof_chapter_ck CHECK ((kind = 'proof') = (chapter_id IS NOT NULL)),
    CONSTRAINT export_job_template_ck
        CHECK (kind = 'source' OR (template_version_id IS NOT NULL AND trim_size IS NOT NULL)),
    CONSTRAINT export_job_output_ck CHECK (status <> 'succeeded' OR output_asset_id IS NOT NULL)
);
CREATE INDEX export_job_book_idx ON export_job (book_id, kind, created_at DESC);
CREATE INDEX export_job_proof_cache_idx ON export_job (chapter_id, cache_key)
    WHERE kind = 'proof' AND status = 'succeeded';
-- one print export at a time per book
CREATE UNIQUE INDEX export_job_live_print_uq ON export_job (book_id)
    WHERE kind = 'print' AND status IN ('queued', 'running');

-- ---------------------------------------------------------------------------
-- Import (Library → Import): one row per run. Undo import deletes the book.
-- ---------------------------------------------------------------------------
CREATE TABLE book_import (
    id                   uuid PRIMARY KEY DEFAULT uuidv7(),
    book_id              uuid REFERENCES book (id) ON DELETE CASCADE,  -- set once the book exists
    source               text NOT NULL CHECK (source IN ('markdown', 'novelcrafter', 'docx', 'codex_zip')),
    file_name            text NOT NULL,
    asset_id             uuid REFERENCES asset (id),  -- the uploaded file
    status               text NOT NULL DEFAULT 'uploaded'
                         CHECK (status IN ('uploaded', 'previewed', 'importing', 'done', 'failed')),
    mapping              jsonb,                -- step 2's summary: counts -> destination, skipped items
    result               jsonb,                -- counts created, for the Plan banner
    error                text,
    banner_dismissed_at  timestamptz,
    created_by           uuid REFERENCES app_user (id) ON DELETE SET NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    finished_at          timestamptz,
    CONSTRAINT book_import_done_ck CHECK (status <> 'done' OR book_id IS NOT NULL)
);
CREATE INDEX book_import_book_idx ON book_import (book_id);
