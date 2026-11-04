-- ---------------------------------------------------------------------------
-- Derived read models (ordering, numbering + word counts are never stored)
-- ---------------------------------------------------------------------------

-- The books whose knowledge base / labels / style guide a book can see: itself + its series.
CREATE VIEW book_scope AS
SELECT id AS book_id, id AS scope_id FROM book
UNION ALL
SELECT id AS book_id, series_id AS scope_id FROM book WHERE series_id IS NOT NULL;

-- Beats in document order (Plan view, AI context, "scene has N beats").
CREATE VIEW beat_outline AS
SELECT
    p.scene_id,
    b.id       AS beat_id,
    m.ord      AS beat_number,
    b.body,
    b.target_word_count,
    b.is_hidden
FROM prose p
CROSS JOIN LATERAL regexp_matches(p.body, '^::beat\{id="([0-9a-fA-F-]{36})"\}[ \t]*$', 'gn')
     WITH ORDINALITY AS m (match, ord)
JOIN beat b ON b.id = m.match[1]::uuid;

-- Plan board / sidebar: display numbers follow position across the whole book.
CREATE VIEW chapter_outline AS
SELECT
    c.id,
    c.book_id,
    c.act_id,
    c.title,
    c.is_numbered,
    dense_rank() OVER (PARTITION BY c.book_id ORDER BY a.position)              AS act_number,
    row_number() OVER (PARTITION BY c.book_id ORDER BY a.position, c.position) AS reading_order,
    CASE WHEN c.is_numbered THEN
        count(*) FILTER (WHERE c.is_numbered)
            OVER (PARTITION BY c.book_id ORDER BY a.position, c.position
                  ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
    END                                                                         AS chapter_number
FROM chapter c
JOIN act a ON a.id = c.act_id
WHERE c.archived_at IS NULL
  AND a.archived_at IS NULL;

-- Live scenes only: a scene in an archived chapter or act is out of the book
-- (its own archived_at stays NULL, so restoring the chapter brings it back).
CREATE VIEW scene_stats AS
SELECT
    s.id                                                                     AS scene_id,
    s.book_id,
    s.chapter_id,
    row_number() OVER (PARTITION BY s.chapter_id ORDER BY s.position)        AS scene_number,
    (SELECT count(*) FROM beat_outline bo WHERE bo.scene_id = s.id)::integer AS beat_count,
    coalesce(p.word_count, 0)                                                AS word_count
FROM scene s
JOIN chapter_outline co ON co.id = s.chapter_id
LEFT JOIN prose p ON p.scene_id = s.id
WHERE s.archived_at IS NULL;

-- Knowledge base additions in document order: an entry's Timeline.
CREATE VIEW kb_progression_outline AS
SELECT
    k.kb_entry_id,
    k.id                                                              AS progression_id,
    k.scene_id,
    ss.chapter_id,
    co.chapter_number,
    ss.scene_number,
    k.body,
    row_number() OVER (PARTITION BY k.kb_entry_id
                       ORDER BY co.reading_order, s.position, m.ord) AS timeline_order
FROM prose p
CROSS JOIN LATERAL regexp_matches(p.body, '^::kb-addition\{id="([0-9a-fA-F-]{36})"\}[ \t]*$', 'gn')
     WITH ORDINALITY AS m (match, ord)
JOIN kb_progression k   ON k.id = m.match[1]::uuid
JOIN scene s            ON s.id = p.scene_id
JOIN scene_stats ss     ON ss.scene_id = s.id
JOIN chapter_outline co ON co.id = s.chapter_id;

-- "Related", both ways.
CREATE VIEW kb_entry_related AS
SELECT entry_a_id AS kb_entry_id, entry_b_id AS related_id, created_at FROM kb_entry_link
UNION ALL
SELECT entry_b_id, entry_a_id, created_at FROM kb_entry_link;

-- "Used in": each live scene that touches a live entry, by kind, with counts.
-- "Name in text" (unlinked name/alias matches) is worked out by the app from
-- kb_entry.name / aliases, like the Write underlines.
CREATE VIEW kb_entry_use AS
WITH uses AS (
    -- mention: [Name](kb:<id>) in prose; printed: ::spell / ::statblock
    SELECT p.scene_id, m[2]::uuid AS kb_entry_id,
           CASE WHEN m[1] = 'kb:' THEN 'mention' ELSE 'printed' END AS kind
    FROM prose p
    CROSS JOIN LATERAL regexp_matches(p.body,
        '(kb:|::(?:spell|statblock)\{kb=")([0-9a-fA-F-]{36})', 'g') AS m
    UNION ALL
    SELECT s.id, m[1]::uuid, 'summary'
    FROM scene s CROSS JOIN LATERAL regexp_matches(coalesce(s.summary, ''), 'kb:([0-9a-fA-F-]{36})', 'g') AS m
    UNION ALL
    SELECT scene_id, kb_entry_id, 'linked' FROM scene_kb
    UNION ALL
    SELECT b.scene_id, m[1]::uuid, 'beat'
    FROM beat b CROSS JOIN LATERAL regexp_matches(b.body, 'kb:([0-9a-fA-F-]{36})', 'g') AS m
    WHERE b.archived_at IS NULL
    UNION ALL
    SELECT b.scene_id, x.kb_entry_id, 'beat_context'
    FROM beat_kb x JOIN beat b ON b.id = x.beat_id AND b.archived_at IS NULL
    UNION ALL
    SELECT scene_id, kb_entry_id, 'timeline' FROM kb_progression_outline
)
SELECT
    u.kb_entry_id,
    u.scene_id,
    ss.book_id,
    ss.chapter_id,
    co.chapter_number,
    ss.scene_number,
    co.reading_order,
    u.kind,
    count(*)::integer AS occurrences
FROM uses u
JOIN kb_entry e         ON e.id = u.kb_entry_id AND e.archived_at IS NULL
JOIN scene_stats ss     ON ss.scene_id = u.scene_id
JOIN chapter_outline co ON co.id = ss.chapter_id
GROUP BY 1, 2, 3, 4, 5, 6, 7, 8;

-- The sidebar's categories for a book: its own and its series', with
-- same-named ones merged ("Same-named categories are merged" on join).
-- Entries keep their own category row and scope; new entries go to own_id
-- when there is one.
CREATE VIEW book_kb_category AS
SELECT
    bs.book_id,
    c.name,
    (array_agg(c.id) FILTER (WHERE c.book_id = bs.book_id))[1]  AS own_id,
    (array_agg(c.id) FILTER (WHERE c.book_id <> bs.book_id))[1] AS series_id,
    array_agg(c.id)                                             AS category_ids,
    (array_agg(c.color ORDER BY c.book_id = bs.book_id))[1]     AS color,   -- the series' look wins
    (array_agg(c.tag ORDER BY c.book_id = bs.book_id))[1]       AS tag,
    min(c.position)                                             AS position
FROM book_scope bs
JOIN kb_category c ON c.book_id = bs.scope_id
GROUP BY bs.book_id, c.name;

-- Library cards: per-book totals, rolled up onto series rows.
CREATE VIEW book_stats AS
WITH per_book AS (
    SELECT
        bk.id,
        bk.series_id,
        bk.status,
        bk.last_edited_at,
        coalesce(sum(ss.word_count), 0)::integer AS word_count
    FROM book bk
    LEFT JOIN scene_stats ss ON ss.book_id = bk.id
    WHERE bk.kind = 'book'
      AND bk.archived_at IS NULL
    GROUP BY bk.id, bk.series_id, bk.status, bk.last_edited_at
),
per_series AS (
    SELECT
        series_id,
        count(*)::integer                                     AS book_count,
        count(*) FILTER (WHERE status = 'published')::integer AS published_count,
        sum(word_count)::integer                              AS word_count,
        max(last_edited_at)                                   AS last_edited_at
    FROM per_book
    WHERE series_id IS NOT NULL
    GROUP BY series_id
)
SELECT
    b.id                                                           AS book_id,
    b.kind,
    coalesce(pb.word_count, ps.word_count, 0)                      AS word_count,
    ps.book_count,
    ps.published_count,
    greatest(b.last_edited_at, ps.last_edited_at)                  AS last_edited_at,
    b.target_word_count,
    CASE WHEN b.target_word_count IS NOT NULL
         THEN round(100.0 * coalesce(pb.word_count, ps.word_count, 0) / b.target_word_count, 1)
    END                                                            AS progress_pct
FROM book b
LEFT JOIN per_book pb   ON pb.id = b.id
LEFT JOIN per_series ps ON ps.series_id = b.id
WHERE b.archived_at IS NULL;

-- Book settings with a series' values filling the book's gaps, then defaults.
CREATE VIEW book_publication_settings AS
SELECT
    b.id                                                        AS book_id,
    coalesce(own.citation_style, ser.citation_style, 'chicago') AS citation_style,
    coalesce(own.rules_name, ser.rules_name, 'Default')         AS rules_name,
    coalesce(own.quote_word_limit, ser.quote_word_limit, 300)   AS quote_word_limit,
    coalesce(own.publisher_name, ser.publisher_name)            AS publisher_name,
    coalesce(own.planned_year, ser.planned_year)                AS planned_year,
    coalesce(own.print_run, ser.print_run)                      AS print_run,
    coalesce(own.formats, ser.formats)                          AS formats,
    coalesce(own.territory, ser.territory)                      AS territory
FROM book b
LEFT JOIN publication_settings own ON own.book_id = b.id
LEFT JOIN publication_settings ser ON ser.book_id = b.series_id
WHERE b.kind = 'book';

-- Book settings → Layout & print and the Layout toolbar: the current
-- template, its pinned version and the newest one it could upgrade to.
CREATE VIEW book_layout_current AS
SELECT
    bl.book_id,
    t.id                 AS template_id,
    t.name               AS template_name,
    t.owner_id IS NOT NULL AS is_user_template,
    v.id                 AS template_version_id,
    v.version            AS pinned_version,
    latest.id            AS latest_version_id,
    latest.version       AS latest_version,
    latest.id <> v.id    AS upgrade_available,
    v.has_preview_css,
    v.manifest -> 'trims' AS trims,
    blt.trim_size,
    blt.options
FROM book_layout bl
JOIN book_layout_template blt   ON blt.book_id = bl.book_id AND blt.template_id = bl.template_id
JOIN layout_template t          ON t.id = bl.template_id
JOIN layout_template_version v  ON v.id = blt.template_version_id
LEFT JOIN LATERAL (
    SELECT lv.id, lv.version FROM layout_template_version lv
    WHERE lv.template_id = t.id AND lv.published_at IS NOT NULL
    ORDER BY lv.major DESC, lv.minor DESC, lv.patch DESC LIMIT 1
) latest ON true;

-- Live uses in book order ("Chapter 4 · Scene 1"), from where their first
-- link sits in the prose. Links in a pending AI draft count too, placed at
-- the draft's ::beat line (between the prose links before and after it).
CREATE VIEW reference_use_outline AS
WITH links AS (
    SELECT p.scene_id, m.match[2]::uuid AS use_id, m.ord::numeric AS ord
    FROM prose p
    CROSS JOIN LATERAL regexp_matches(p.body,
        '\[((?:[^\[\]\\]|\\.)*)\]\(ref:([0-9a-fA-F-]{36})\)', 'g')
        WITH ORDINALITY AS m (match, ord)
    UNION ALL
    SELECT g.scene_id, m.match[2]::uuid,
           (SELECT count(*) FROM regexp_matches(
                left(p.body, strpos(p.body, '::beat{id="' || g.beat_id || '"}')),
                '\]\(ref:[0-9a-fA-F-]{36}\)', 'g')) + 0.5 + m.ord / 100000.0
    FROM ai_generation g
    JOIN prose p ON p.scene_id = g.scene_id
    CROSS JOIN LATERAL regexp_matches(coalesce(g.output, ''),
        '\[((?:[^\[\]\\]|\\.)*)\]\(ref:([0-9a-fA-F-]{36})\)', 'g')
        WITH ORDINALITY AS m (match, ord)
    WHERE g.status IN ('streaming', 'pending')
),
first_link AS (
    SELECT scene_id, use_id, min(ord) AS ord FROM links GROUP BY 1, 2
)
SELECT
    u.id               AS use_id,
    u.reference_id,
    u.book_id,
    u.scene_id,
    s.chapter_id,
    co.chapter_number,
    ss.scene_number,
    u.kind,
    u.word_count,
    u.locator,
    u.changed_at,
    row_number() OVER (PARTITION BY u.book_id ORDER BY co.reading_order, s.position, f.ord) AS book_order
FROM reference_use u
JOIN first_link f       ON f.use_id = u.id AND f.scene_id = u.scene_id
JOIN scene s            ON s.id = u.scene_id AND s.archived_at IS NULL
JOIN chapter_outline co ON co.id = s.chapter_id
JOIN scene_stats ss     ON ss.scene_id = s.id
WHERE u.archived_at IS NULL;

-- Per-source totals and its marker number: the order of first use in the
-- book, the same number Write shows. Uncited sources have no number.
CREATE VIEW reference_stats AS
SELECT
    r.id                                                                    AS reference_id,
    r.book_id,
    CASE WHEN count(o.use_id) > 0 THEN
        dense_rank() OVER (PARTITION BY r.book_id
                           ORDER BY min(o.book_order) NULLS LAST)
    END                                                                     AS number,
    count(o.use_id)::integer                                                AS use_count,
    coalesce(sum(o.word_count) FILTER (WHERE o.kind = 'quote'), 0)::integer AS quoted_words,
    coalesce(sum(o.word_count) FILTER (WHERE o.kind = 'lyric'), 0)::integer AS lyric_words,
    count(o.use_id) FILTER (WHERE o.kind = 'figure')::integer               AS figure_count,
    count(o.use_id) FILTER (WHERE o.kind = 'data')::integer                 AS data_count,
    array_agg(DISTINCT o.chapter_number) FILTER (WHERE o.chapter_number IS NOT NULL)
                                                                            AS chapter_numbers,
    (array_agg(o.chapter_id ORDER BY o.book_order))[1]                      AS first_chapter_id,
    count(o.use_id) FILTER (WHERE o.changed_at > r.signed_off_at)::integer  AS uses_since_signoff
FROM reference r
LEFT JOIN reference_use_outline o ON o.reference_id = r.id
WHERE r.archived_at IS NULL
GROUP BY r.id, r.book_id, r.signed_off_at;

-- Clearance is worked out, never stored. Reasons are codes the UI words.
--   not_cited    no live use
--   risk         a use needs permission that isn't granted
--   needs_check  a check warns, not signed off yet, or changed since sign-off
--   cleared      otherwise
CREATE VIEW reference_clearance AS
WITH base AS (
    SELECT
        st.*,
        r.licence,
        l.allows_reuse,
        l.needs_fair_use_decision AND r.fair_use_decided_at IS NULL     AS fair_use_open,
        r.signed_off_at,
        ps.quote_word_limit,
        ARRAY(SELECT f FROM unnest(k.required_fields) AS f
              WHERE coalesce(nullif(btrim(to_jsonb(r) ->> f), ''), '[]') = '[]') AS missing_fields,
        EXISTS (SELECT 1 FROM reference_check c
                WHERE c.reference_id = r.id
                  AND c.status IN ('redirect', 'not_found', 'error')
                  AND c.checked_value = to_jsonb(r) ->> c.field)        AS identifier_failing,
        pr.status                                                       AS permission_status
    FROM reference r
    JOIN reference_stats st            ON st.reference_id = r.id
    JOIN reference_kind k              ON k.code = r.kind
    JOIN licence l                     ON l.code = r.licence
    JOIN book_publication_settings ps  ON ps.book_id = r.book_id
    LEFT JOIN LATERAL (
        SELECT p.status FROM permission_request p
        WHERE p.reference_id = r.id AND p.status <> 'withdrawn'
        ORDER BY p.created_at DESC LIMIT 1
    ) pr ON true
),
checked AS (
    SELECT
        b.*,
        CASE WHEN licence = 'public_domain'                  THEN NULL
             WHEN quoted_words > quote_word_limit            THEN 'quote_over_limit'
             WHEN lyric_words > 0                            THEN 'lyric'
             WHEN figure_count > 0 AND NOT allows_reuse      THEN 'figure'
        END AS permission_reason,
        CASE WHEN licence = 'unknown'                        THEN 'licence_unknown'
             WHEN fair_use_open                              THEN 'fair_use_undecided'
             WHEN cardinality(missing_fields) > 0            THEN 'citation_incomplete'
             WHEN identifier_failing                         THEN 'identifier_failing'
        END AS warning
    FROM base b
)
SELECT
    reference_id,
    book_id,
    number,
    use_count,
    quoted_words,
    lyric_words,
    figure_count,
    data_count,
    missing_fields,
    permission_reason IS NOT NULL         AS needs_permission,
    permission_status,
    CASE
        WHEN use_count = 0                                                     THEN 'not_cited'
        WHEN permission_reason IS NOT NULL
             AND permission_status IS DISTINCT FROM 'granted'                  THEN 'risk'
        WHEN warning IS NOT NULL OR signed_off_at IS NULL
             OR uses_since_signoff > 0                                         THEN 'needs_check'
        ELSE 'cleared'
    END AS status,
    CASE
        WHEN use_count = 0                                                     THEN 'no_markers'
        WHEN permission_reason IS NOT NULL
             AND permission_status IS DISTINCT FROM 'granted'                  THEN
            CASE permission_status
                WHEN 'draft'   THEN 'permission_drafted'
                WHEN 'sent'    THEN 'permission_sent'
                WHEN 'refused' THEN 'permission_refused'
                ELSE permission_reason
            END
        WHEN warning IS NOT NULL                                               THEN warning
        WHEN signed_off_at IS NULL                                             THEN 'ready_to_sign_off'
        WHEN uses_since_signoff > 0                                            THEN 'new_uses_since_signoff'
        ELSE 'signed_off'
    END AS reason
FROM checked;

-- Summary strip, tab badge and the print-export gate (#13).
CREATE VIEW book_reference_summary AS
SELECT
    b.id                                                               AS book_id,
    count(c.reference_id)::integer                                     AS source_count,
    count(*) FILTER (WHERE c.status = 'risk')::integer                 AS risk_count,
    count(*) FILTER (WHERE c.status = 'needs_check')::integer          AS needs_check_count,
    count(*) FILTER (WHERE c.status = 'cleared')::integer              AS cleared_count,
    count(*) FILTER (WHERE c.status = 'not_cited')::integer            AS not_cited_count,
    coalesce(sum(c.use_count), 0)::integer                             AS citation_count,
    coalesce(sum(c.quoted_words + c.lyric_words), 0)::integer          AS quoted_words,
    (SELECT count(*) FROM quote_flag f
     JOIN scene_stats ss ON ss.scene_id = f.scene_id
     WHERE f.book_id = b.id AND f.status = 'open')::integer            AS open_quote_flags,
    count(*) FILTER (WHERE c.status = 'risk') > 0                      AS blocks_print_export
FROM book b
LEFT JOIN reference_clearance c ON c.book_id = b.id
WHERE b.kind = 'book'
  AND b.archived_at IS NULL
GROUP BY b.id;

-- CSL-JSON per source: feeds citeproc (Chicago / APA / Harvard), the
-- bibliography and BibTeX / RIS / CSL export. Credit lines print after it.
CREATE VIEW reference_csl AS
SELECT
    r.id AS reference_id,
    r.book_id,
    r.csl_extra || jsonb_strip_nulls(jsonb_build_object(
        'id',              coalesce(r.cite_key, r.id::text),
        'type',            k.csl_type,
        'title',           nullif(r.title, ''),
        'author',          r.authors,
        'issued',          CASE WHEN r.year IS NOT NULL
                                THEN jsonb_build_object('date-parts', jsonb_build_array(jsonb_build_array(r.year))) END,
        'publisher',       r.publisher,
        'container-title', r.container_title,
        'volume',          r.volume,
        'issue',           r.issue,
        'page',            r.page_range,
        'edition',         r.edition,
        'ISBN',            r.isbn,
        'DOI',             r.doi,
        'URL',             r.url,
        'accessed',        CASE WHEN r.accessed_on IS NOT NULL
                                THEN jsonb_build_object('date-parts', jsonb_build_array(jsonb_build_array(
                                    extract(year FROM r.accessed_on)::integer,
                                    extract(month FROM r.accessed_on)::integer,
                                    extract(day FROM r.accessed_on)::integer))) END
    )) AS csl,
    (SELECT p.credit_line FROM permission_request p
     WHERE p.reference_id = r.id AND p.status = 'granted') AS credit_line
FROM reference r
JOIN reference_kind k ON k.code = r.kind
WHERE r.archived_at IS NULL;

-- ---------------------------------------------------------------------------
-- Triggers
-- ---------------------------------------------------------------------------
CREATE FUNCTION set_updated_at() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'app_user', 'book', 'act', 'chapter', 'scene', 'prose', 'beat', 'figure',
        'kb_category', 'kb_entry', 'kb_progression', 'label',
        'publication_settings', 'reference', 'reference_use', 'permission_request',
        'layout_template', 'book_layout_template', 'book_layout'
    ] LOOP
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION set_updated_at()',
            t || '_set_updated_at', t);
    END LOOP;
END;
$$;

-- Library sorts and labels books by last_edited_at. Any manuscript save bumps
-- it, at most once a minute so autosave doesn't rewrite the book row each time.
CREATE FUNCTION touch_book_last_edited() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_row   record;
    v_book  uuid;
BEGIN
    IF TG_OP = 'DELETE' THEN v_row := OLD; ELSE v_row := NEW; END IF;
    IF TG_TABLE_NAME IN ('act', 'chapter', 'scene') THEN
        v_book := v_row.book_id;
    ELSE  -- prose, beat, figure, kb_progression
        SELECT book_id INTO v_book FROM scene WHERE id = v_row.scene_id;
    END IF;
    UPDATE book SET last_edited_at = now()
    WHERE id = v_book AND last_edited_at < now() - interval '1 minute';
    RETURN NULL;
END;
$$;

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['act', 'chapter', 'scene', 'prose', 'beat', 'figure', 'kb_progression'] LOOP
        EXECUTE format(
            'CREATE TRIGGER %I AFTER INSERT OR UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION touch_book_last_edited()',
            t || '_touch_book', t);
    END LOOP;
END;
$$;

-- Slugs: set from the name on insert (made unique within the book), then
-- fixed. Renaming an entry or chapter keeps its e:/ch: handle.
CREATE FUNCTION set_slug() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_base  text;
    v_n     integer := 1;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF NEW.slug IS DISTINCT FROM OLD.slug THEN
            RAISE EXCEPTION '%.slug never changes (% -> %)', TG_TABLE_NAME, OLD.slug, NEW.slug
                USING ERRCODE = 'check_violation';
        END IF;
        RETURN NEW;
    END IF;
    IF NEW.slug IS NOT NULL THEN
        RETURN NEW;
    END IF;
    IF TG_TABLE_NAME = 'chapter' THEN
        v_base := slugify(NEW.title);
    ELSE
        v_base := slugify(NEW.name);
    END IF;
    NEW.slug := v_base;
    LOOP
        EXIT WHEN TG_TABLE_NAME = 'chapter'
                  AND NOT EXISTS (SELECT 1 FROM chapter WHERE book_id = NEW.book_id AND slug = NEW.slug);
        EXIT WHEN TG_TABLE_NAME = 'kb_entry'
                  AND NOT EXISTS (SELECT 1 FROM kb_entry WHERE book_id = NEW.book_id AND slug = NEW.slug);
        v_n := v_n + 1;
        NEW.slug := v_base || '-' || v_n;
    END LOOP;
    RETURN NEW;
END;
$$;

CREATE TRIGGER chapter_set_slug BEFORE INSERT OR UPDATE OF slug ON chapter
    FOR EACH ROW EXECUTE FUNCTION set_slug();
CREATE TRIGGER kb_entry_set_slug BEFORE INSERT OR UPDATE OF slug ON kb_entry
    FOR EACH ROW EXECUTE FUNCTION set_slug();

-- Every book has at least one act: create one with the book, untitled
-- ("Name this act") ...
CREATE FUNCTION book_create_default_act() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO act (book_id, position, title) VALUES (NEW.id, 'a0', '');
    RETURN NULL;
END;
$$;

CREATE TRIGGER book_create_default_act
    AFTER INSERT ON book
    FOR EACH ROW WHEN (NEW.kind = 'book')
    EXECUTE FUNCTION book_create_default_act();

-- ... and refuse to commit a live book with no live acts. Deferred, so the
-- last act can be replaced inside one transaction; skipped when the book
-- itself is being deleted or archived.
CREATE FUNCTION act_keep_one() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM book WHERE id = OLD.book_id AND archived_at IS NULL)
       AND NOT EXISTS (SELECT 1 FROM act WHERE book_id = OLD.book_id AND archived_at IS NULL) THEN
        RAISE EXCEPTION 'book % must have at least one act', OLD.book_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER act_keep_one
    AFTER DELETE OR UPDATE OF archived_at, book_id ON act
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION act_keep_one();

-- Objects embedded in a prose body must reference a row of this scene, once.
-- ::spell / ::statblock may repeat but must name an entry in the book's
-- scope. Source-reference links must point at a use of this scene (a quote
-- that spans paragraphs repeats its id).
CREATE FUNCTION prose_check_embeds() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    r       record;
    v_use   uuid;
    v_book  uuid;
BEGIN
    FOR r IN
        SELECT m[1] AS node_type, m[2]::uuid AS ref_id, count(*) AS uses
        FROM regexp_matches(NEW.body,
                 '^::(beat|kb-addition|figure)\{id="([0-9a-fA-F-]{36})"\}[ \t]*$', 'gn') AS m
        GROUP BY 1, 2
    LOOP
        IF r.uses > 1 THEN
            RAISE EXCEPTION '% % appears % times in scene %', r.node_type, r.ref_id, r.uses, NEW.scene_id;
        ELSIF r.node_type = 'beat' AND NOT EXISTS (
                  SELECT 1 FROM beat WHERE id = r.ref_id AND scene_id = NEW.scene_id) THEN
            RAISE EXCEPTION 'beat % does not belong to scene %', r.ref_id, NEW.scene_id;
        ELSIF r.node_type = 'kb-addition' AND NOT EXISTS (
                  SELECT 1 FROM kb_progression WHERE id = r.ref_id AND scene_id = NEW.scene_id) THEN
            RAISE EXCEPTION 'knowledge base addition % does not belong to scene %', r.ref_id, NEW.scene_id;
        ELSIF r.node_type = 'figure' AND NOT EXISTS (
                  SELECT 1 FROM figure WHERE id = r.ref_id AND scene_id = NEW.scene_id) THEN
            RAISE EXCEPTION 'figure % does not belong to scene %', r.ref_id, NEW.scene_id;
        END IF;
    END LOOP;

    SELECT book_id INTO v_book FROM scene WHERE id = NEW.scene_id;
    FOR r IN
        SELECT DISTINCT m[1] AS node_type, m[2]::uuid AS entry_id
        FROM regexp_matches(NEW.body,
                 '^::(spell|statblock)\{kb="([0-9a-fA-F-]{36})"\}[ \t]*$', 'gn') AS m
    LOOP
        IF NOT EXISTS (SELECT 1 FROM kb_entry e JOIN book_scope bs ON bs.scope_id = e.book_id
                       WHERE e.id = r.entry_id AND bs.book_id = v_book) THEN
            RAISE EXCEPTION '% entry % is outside the scope of book %', r.node_type, r.entry_id, v_book
                USING ERRCODE = 'foreign_key_violation';
        END IF;
    END LOOP;

    FOR v_use IN
        SELECT DISTINCT m[2]::uuid
        FROM regexp_matches(NEW.body,
                 '\[((?:[^\[\]\\]|\\.)*)\]\(ref:([0-9a-fA-F-]{36})\)', 'g') AS m
    LOOP
        IF NOT EXISTS (SELECT 1 FROM reference_use WHERE id = v_use AND scene_id = NEW.scene_id) THEN
            RAISE EXCEPTION 'reference use % does not belong to scene %', v_use, NEW.scene_id;
        END IF;
    END LOOP;
    RETURN NEW;
END;
$$;

CREATE TRIGGER prose_check_embeds
    BEFORE INSERT OR UPDATE OF body ON prose
    FOR EACH ROW EXECUTE FUNCTION prose_check_embeds();

-- Keep embedded rows in step with the document: deleting a ::beat line
-- archives the beat, undo (line comes back) restores it. Same for knowledge
-- base additions, figures and source-reference uses, whose excerpt is also
-- refreshed from the linked words. A use whose link is only in a pending AI
-- draft is left alone.
CREATE FUNCTION prose_sync_embeds() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_beats      uuid[];
    v_additions  uuid[];
    v_figures    uuid[];
    v_uses       uuid[];
BEGIN
    SELECT coalesce(array_agg(m[2]::uuid) FILTER (WHERE m[1] = 'beat'), '{}'),
           coalesce(array_agg(m[2]::uuid) FILTER (WHERE m[1] = 'kb-addition'), '{}'),
           coalesce(array_agg(m[2]::uuid) FILTER (WHERE m[1] = 'figure'), '{}')
    INTO v_beats, v_additions, v_figures
    FROM regexp_matches(NEW.body,
             '^::(beat|kb-addition|figure)\{id="([0-9a-fA-F-]{36})"\}[ \t]*$', 'gn') AS m;

    UPDATE beat SET archived_at = CASE WHEN id = ANY (v_beats) THEN NULL ELSE now() END
    WHERE scene_id = NEW.scene_id
      AND (archived_at IS NULL) <> (id = ANY (v_beats));

    UPDATE kb_progression SET archived_at = CASE WHEN id = ANY (v_additions) THEN NULL ELSE now() END
    WHERE scene_id = NEW.scene_id
      AND (archived_at IS NULL) <> (id = ANY (v_additions));

    UPDATE figure SET archived_at = CASE WHEN id = ANY (v_figures) THEN NULL ELSE now() END
    WHERE scene_id = NEW.scene_id
      AND (archived_at IS NULL) <> (id = ANY (v_figures));

    WITH links AS (
        SELECT m.match[2]::uuid AS use_id,
               string_agg(m.match[1], E'\n\n' ORDER BY m.ord) AS excerpt
        FROM regexp_matches(NEW.body,
                 '\[((?:[^\[\]\\]|\\.)*)\]\(ref:([0-9a-fA-F-]{36})\)', 'g')
             WITH ORDINALITY AS m (match, ord)
        GROUP BY 1
    ),
    synced AS (
        UPDATE reference_use u SET excerpt = l.excerpt, archived_at = NULL
        FROM links l
        WHERE u.id = l.use_id
          AND (u.excerpt IS DISTINCT FROM l.excerpt OR u.archived_at IS NOT NULL)
    )
    SELECT coalesce(array_agg(use_id), '{}') INTO v_uses FROM links;

    UPDATE reference_use u SET archived_at = now()
    WHERE u.scene_id = NEW.scene_id
      AND u.archived_at IS NULL
      AND u.id <> ALL (v_uses)
      AND NOT EXISTS (SELECT 1 FROM ai_generation g
                      WHERE g.id = u.generation_id AND g.status IN ('streaming', 'pending'));

    RETURN NULL;
END;
$$;

CREATE TRIGGER prose_sync_embeds
    AFTER INSERT OR UPDATE OF body ON prose
    FOR EACH ROW EXECUTE FUNCTION prose_sync_embeds();

-- A draft's source uses: Apply makes them ordinary prose uses (the app writes
-- the output into prose.body first); Discard or failure archives the ones
-- whose link didn't reach the prose.
CREATE FUNCTION ai_generation_resolve_uses() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.status = 'applied' THEN
        UPDATE reference_use SET generation_id = NULL WHERE generation_id = NEW.id;
    ELSE
        UPDATE reference_use u SET archived_at = now(), generation_id = NULL
        WHERE u.generation_id = NEW.id
          AND u.archived_at IS NULL
          AND NOT EXISTS (SELECT 1 FROM prose p
                          WHERE p.scene_id = u.scene_id AND strpos(p.body, '(ref:' || u.id || ')') > 0);
        UPDATE reference_use SET generation_id = NULL WHERE generation_id = NEW.id;
    END IF;
    RETURN NULL;
END;
$$;

CREATE TRIGGER ai_generation_resolve_uses
    AFTER UPDATE OF status ON ai_generation
    FOR EACH ROW
    WHEN (OLD.status IN ('streaming', 'pending') AND NEW.status IN ('applied', 'discarded', 'failed'))
    EXECUTE FUNCTION ai_generation_resolve_uses();

-- Knowledge base entries, labels and assets linked from a book's rows must be
-- in the book's scope: the book itself or its series.
CREATE FUNCTION check_link_scope() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_book   uuid;
    v_scope  uuid;
BEGIN
    IF TG_TABLE_NAME IN ('reference', 'act', 'chapter') THEN
        v_book := NEW.book_id;
    ELSIF TG_TABLE_NAME = 'book' THEN
        v_book := NEW.id;
    ELSIF TG_TABLE_NAME = 'beat_kb' THEN
        SELECT s.book_id INTO v_book FROM beat b JOIN scene s ON s.id = b.scene_id WHERE b.id = NEW.beat_id;
    ELSIF TG_TABLE_NAME = 'kb_entry_link' THEN
        SELECT book_id INTO v_book FROM kb_entry WHERE id = NEW.entry_a_id;
    ELSE
        SELECT s.book_id INTO v_book FROM scene s WHERE s.id = NEW.scene_id;
    END IF;

    IF TG_TABLE_NAME = 'scene_label' THEN
        SELECT l.book_id INTO v_scope FROM label l WHERE l.id = NEW.label_id;
    ELSIF TG_TABLE_NAME = 'figure' THEN
        SELECT a.book_id INTO v_scope FROM asset a WHERE a.id = NEW.asset_id;
    ELSIF TG_TABLE_NAME IN ('act', 'chapter') THEN
        SELECT a.book_id INTO v_scope FROM asset a WHERE a.id = NEW.opener_asset_id;
    ELSIF TG_TABLE_NAME = 'book' THEN
        SELECT a.book_id INTO v_scope FROM asset a WHERE a.id = NEW.cover_asset_id;
    ELSIF TG_TABLE_NAME = 'kb_entry_link' THEN
        SELECT book_id INTO v_scope FROM kb_entry WHERE id = NEW.entry_b_id;
    ELSE
        SELECT e.book_id INTO v_scope FROM kb_entry e WHERE e.id = NEW.kb_entry_id;
    END IF;

    IF v_book IS NOT NULL AND v_scope IS NOT NULL  -- missing rows are the FK's job
       AND NOT EXISTS (SELECT 1 FROM book_scope WHERE book_id = v_book AND scope_id = v_scope)
       -- a link between two entries is fine when either side's scope holds the other
       AND NOT (TG_TABLE_NAME = 'kb_entry_link'
                AND EXISTS (SELECT 1 FROM book_scope WHERE book_id = v_scope AND scope_id = v_book)) THEN
        RAISE EXCEPTION '%: % is outside the scope of book %', TG_TABLE_NAME, v_scope, v_book
            USING ERRCODE = 'foreign_key_violation';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER scene_kb_scope BEFORE INSERT OR UPDATE ON scene_kb
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER beat_kb_scope BEFORE INSERT OR UPDATE ON beat_kb
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER kb_progression_scope BEFORE INSERT OR UPDATE OF kb_entry_id, scene_id ON kb_progression
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER scene_label_scope BEFORE INSERT OR UPDATE ON scene_label
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER reference_kb_scope BEFORE INSERT OR UPDATE OF kb_entry_id ON reference
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER kb_entry_link_scope BEFORE INSERT OR UPDATE ON kb_entry_link
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER figure_scope BEFORE INSERT OR UPDATE OF asset_id, scene_id ON figure
    FOR EACH ROW EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER act_opener_scope BEFORE INSERT OR UPDATE OF opener_asset_id ON act
    FOR EACH ROW WHEN (NEW.opener_asset_id IS NOT NULL) EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER chapter_opener_scope BEFORE INSERT OR UPDATE OF opener_asset_id ON chapter
    FOR EACH ROW WHEN (NEW.opener_asset_id IS NOT NULL) EXECUTE FUNCTION check_link_scope();
CREATE TRIGGER book_cover_scope BEFORE INSERT OR UPDATE OF cover_asset_id ON book
    FOR EACH ROW WHEN (NEW.cover_asset_id IS NOT NULL) EXECUTE FUNCTION check_link_scope();

-- Deleting an entry is a soft delete (archived_at) so Undo works; the app
-- first strips its ::kb-addition lines, which archives its timeline notes. A
-- hard delete while a note's line is still in the prose would leave the
-- document pointing at nothing, so it is refused.
CREATE FUNCTION kb_entry_guard_delete() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM kb_progression WHERE kb_entry_id = OLD.id AND archived_at IS NULL) THEN
        RAISE EXCEPTION 'kb_entry % still has timeline notes in the prose; remove their lines first', OLD.id
            USING ERRCODE = 'foreign_key_violation';
    END IF;
    RETURN OLD;
END;
$$;

CREATE TRIGGER kb_entry_guard_delete BEFORE DELETE ON kb_entry
    FOR EACH ROW EXECUTE FUNCTION kb_entry_guard_delete();

-- A trim must be one the pinned template version supports.
CREATE FUNCTION book_layout_check_trim() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM layout_template_version v
                   WHERE v.id = NEW.template_version_id AND v.manifest -> 'trims' ? NEW.trim_size) THEN
        RAISE EXCEPTION 'trim % is not supported by template version %', NEW.trim_size, NEW.template_version_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER book_layout_check_trim
    BEFORE INSERT OR UPDATE OF template_version_id, trim_size ON book_layout_template
    FOR EACH ROW EXECUTE FUNCTION book_layout_check_trim();

-- A use that is added, re-typed or lengthened reopens a signed-off source.
-- Shortening it, or restoring it with undo, does not.
CREATE FUNCTION reference_use_touch() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.kind IS DISTINCT FROM OLD.kind
       OR markdown_word_count(NEW.excerpt) > markdown_word_count(OLD.excerpt) THEN
        NEW.changed_at := now();
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER reference_use_touch
    BEFORE UPDATE OF kind, excerpt ON reference_use
    FOR EACH ROW EXECUTE FUNCTION reference_use_touch();

-- Restoring an archived scene (Plan → Archived → Restore) brings its source
-- uses back. Any source signed off while the scene was away hasn't reviewed
-- them, so they count as new.
CREATE FUNCTION scene_restore_reopens_signoff() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE reference_use u SET changed_at = now()
    FROM reference r
    WHERE u.scene_id = NEW.id
      AND u.archived_at IS NULL
      AND r.id = u.reference_id
      AND r.signed_off_at > OLD.archived_at;
    RETURN NULL;
END;
$$;

CREATE TRIGGER scene_restore_reopens_signoff
    AFTER UPDATE OF archived_at ON scene
    FOR EACH ROW WHEN (OLD.archived_at IS NOT NULL AND NEW.archived_at IS NULL)
    EXECUTE FUNCTION scene_restore_reopens_signoff();

-- When a book leaves (or switches) its series, copy the series' knowledge
-- base categories, entries, links and labels, and the series assets the book
-- uses, down onto the book. Then repoint the book's rows (sources included)
-- and the kb:<uuid> / ::spell{kb=…} ids in its text at the copies.
-- Same-named book categories / labels are reused. The series' publication
-- settings fill the book's empty ones.
CREATE FUNCTION book_copy_down_series_scope() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_map  jsonb;
BEGIN
    INSERT INTO kb_category (book_id, name, tag, color, icon, position, field_defs)
    SELECT NEW.id, c.name, c.tag, c.color, c.icon, c.position, c.field_defs
    FROM kb_category c
    WHERE c.book_id = OLD.series_id
    ON CONFLICT (book_id, name) DO NOTHING;

    -- a copy keeps its slug unless the book already uses it (set_slug suffixes it)
    INSERT INTO kb_entry (book_id, category_id, name, slug, position, aliases, tags, summary, body,
                          fields, custom_fields, always_in_context, exclude_from_auto_context,
                          import_url, import_archive_url, imported_at, copied_from_id,
                          external_id, archived_at)
    SELECT NEW.id, bc.id, e.name,
           CASE WHEN EXISTS (SELECT 1 FROM kb_entry x WHERE x.book_id = NEW.id AND x.slug = e.slug)
                THEN NULL ELSE e.slug END,
           e.position, e.aliases, e.tags, e.summary, e.body, e.fields, e.custom_fields,
           e.always_in_context, e.exclude_from_auto_context,
           e.import_url, e.import_archive_url, e.imported_at, e.id, e.external_id, e.archived_at
    FROM kb_entry e
    JOIN kb_category sc ON sc.id = e.category_id
    JOIN kb_category bc ON bc.book_id = NEW.id AND bc.name = sc.name
    WHERE e.book_id = OLD.series_id;

    SELECT coalesce(jsonb_object_agg(copied_from_id::text, id::text), '{}') INTO v_map
    FROM kb_entry WHERE book_id = NEW.id AND copied_from_id IN (
        SELECT id FROM kb_entry WHERE book_id = OLD.series_id);

    -- Related: series↔series pairs are copied, book↔series pairs repointed
    INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
    SELECT least(ca.id, cb.id), greatest(ca.id, cb.id)
    FROM kb_entry_link l
    JOIN kb_entry ca ON ca.book_id = NEW.id AND ca.copied_from_id = l.entry_a_id
    JOIN kb_entry cb ON cb.book_id = NEW.id AND cb.copied_from_id = l.entry_b_id
    ON CONFLICT DO NOTHING;

    WITH moved AS (
        DELETE FROM kb_entry_link l
        USING kb_entry own, kb_entry ser
        WHERE own.book_id = NEW.id AND ser.book_id = OLD.series_id
          AND ((l.entry_a_id = own.id AND l.entry_b_id = ser.id)
            OR (l.entry_b_id = own.id AND l.entry_a_id = ser.id))
        RETURNING own.id AS own_id, (v_map ->> ser.id::text)::uuid AS copy_id, l.created_at
    )
    INSERT INTO kb_entry_link (entry_a_id, entry_b_id, created_at)
    SELECT least(own_id, copy_id), greatest(own_id, copy_id), created_at FROM moved
    WHERE copy_id IS NOT NULL
    ON CONFLICT DO NOTHING;

    UPDATE scene_kb x SET kb_entry_id = ce.id
    FROM kb_entry ce, scene s
    WHERE ce.book_id = NEW.id AND ce.copied_from_id = x.kb_entry_id
      AND s.id = x.scene_id AND s.book_id = NEW.id;

    UPDATE beat_kb x SET kb_entry_id = ce.id
    FROM kb_entry ce, beat b, scene s
    WHERE ce.book_id = NEW.id AND ce.copied_from_id = x.kb_entry_id
      AND b.id = x.beat_id AND s.id = b.scene_id AND s.book_id = NEW.id;

    UPDATE kb_progression x SET kb_entry_id = ce.id
    FROM kb_entry ce, scene s
    WHERE ce.book_id = NEW.id AND ce.copied_from_id = x.kb_entry_id
      AND s.id = x.scene_id AND s.book_id = NEW.id;

    UPDATE reference x SET kb_entry_id = ce.id
    FROM kb_entry ce
    WHERE ce.book_id = NEW.id AND ce.copied_from_id = x.kb_entry_id
      AND x.book_id = NEW.id;

    -- mentions and print blocks in the book's text
    IF v_map <> '{}' THEN
        UPDATE prose p SET body = remap_uuids(p.body, v_map)
        FROM scene s WHERE s.id = p.scene_id AND s.book_id = NEW.id
          AND p.body IS DISTINCT FROM remap_uuids(p.body, v_map);
        UPDATE scene SET summary = remap_uuids(summary, v_map), plan_notes = remap_uuids(plan_notes, v_map)
        WHERE book_id = NEW.id
          AND (summary, plan_notes) IS DISTINCT FROM (remap_uuids(summary, v_map), remap_uuids(plan_notes, v_map));
        UPDATE beat b SET body = remap_uuids(b.body, v_map)
        FROM scene s WHERE s.id = b.scene_id AND s.book_id = NEW.id
          AND b.body IS DISTINCT FROM remap_uuids(b.body, v_map);
        UPDATE kb_progression k SET body = remap_uuids(k.body, v_map)
        FROM scene s WHERE s.id = k.scene_id AND s.book_id = NEW.id
          AND k.body IS DISTINCT FROM remap_uuids(k.body, v_map);
        UPDATE chapter SET summary = remap_uuids(summary, v_map), notes = remap_uuids(notes, v_map)
        WHERE book_id = NEW.id
          AND (summary, notes) IS DISTINCT FROM (remap_uuids(summary, v_map), remap_uuids(notes, v_map));
        UPDATE act SET summary = remap_uuids(summary, v_map)
        WHERE book_id = NEW.id AND summary IS DISTINCT FROM remap_uuids(summary, v_map);
        UPDATE kb_entry SET summary = remap_uuids(summary, v_map), body = remap_uuids(body, v_map)
        WHERE book_id = NEW.id
          AND (summary, body) IS DISTINCT FROM (remap_uuids(summary, v_map), remap_uuids(body, v_map));
        UPDATE book SET style_guide = remap_uuids(style_guide, v_map)
        WHERE id = NEW.id AND style_guide IS DISTINCT FROM remap_uuids(style_guide, v_map);
    END IF;

    -- series assets the book uses: figures, openers, cover
    INSERT INTO asset (owner_id, book_id, kind, filename, mime, bytes, width_px, height_px,
                       storage_key, meta, copied_from_id, created_at, archived_at)
    SELECT a.owner_id, NEW.id, a.kind, a.filename, a.mime, a.bytes, a.width_px, a.height_px,
           a.storage_key, a.meta, a.id, a.created_at, a.archived_at
    FROM asset a
    WHERE a.book_id = OLD.series_id
      AND a.id IN (SELECT f.asset_id FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = NEW.id
                   UNION SELECT opener_asset_id FROM chapter WHERE book_id = NEW.id
                   UNION SELECT opener_asset_id FROM act WHERE book_id = NEW.id
                   UNION SELECT NEW.cover_asset_id);

    INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key, created_at)
    SELECT ca.id, r.purpose, r.mime, r.width_px, r.height_px, r.bytes, r.storage_key, r.created_at
    FROM asset ca JOIN asset_rendition r ON r.asset_id = ca.copied_from_id
    WHERE ca.book_id = NEW.id AND ca.copied_from_id IS NOT NULL
    ON CONFLICT DO NOTHING;

    UPDATE figure f SET asset_id = ca.id
    FROM asset ca, scene s
    WHERE ca.book_id = NEW.id AND ca.copied_from_id = f.asset_id
      AND s.id = f.scene_id AND s.book_id = NEW.id;
    UPDATE chapter c SET opener_asset_id = ca.id
    FROM asset ca WHERE c.book_id = NEW.id AND ca.book_id = NEW.id AND ca.copied_from_id = c.opener_asset_id;
    UPDATE act a SET opener_asset_id = ca.id
    FROM asset ca WHERE a.book_id = NEW.id AND ca.book_id = NEW.id AND ca.copied_from_id = a.opener_asset_id;
    UPDATE book b SET cover_asset_id = ca.id
    FROM asset ca WHERE b.id = NEW.id AND ca.book_id = NEW.id AND ca.copied_from_id = b.cover_asset_id;

    -- the series' publication settings fill the book's gaps
    INSERT INTO publication_settings AS ps
        (book_id, citation_style, rules_name, quote_word_limit, publisher_name,
         planned_year, print_run, formats, territory)
    SELECT NEW.id, s.citation_style, s.rules_name, s.quote_word_limit, s.publisher_name,
           s.planned_year, s.print_run, s.formats, s.territory
    FROM publication_settings s
    WHERE s.book_id = OLD.series_id
    ON CONFLICT (book_id) DO UPDATE SET
        citation_style   = coalesce(ps.citation_style, EXCLUDED.citation_style),
        rules_name       = coalesce(ps.rules_name, EXCLUDED.rules_name),
        quote_word_limit = coalesce(ps.quote_word_limit, EXCLUDED.quote_word_limit),
        publisher_name   = coalesce(ps.publisher_name, EXCLUDED.publisher_name),
        planned_year     = coalesce(ps.planned_year, EXCLUDED.planned_year),
        print_run        = coalesce(ps.print_run, EXCLUDED.print_run),
        formats          = coalesce(ps.formats, EXCLUDED.formats),
        territory        = coalesce(ps.territory, EXCLUDED.territory);

    INSERT INTO label (book_id, name, color, position)
    SELECT NEW.id, l.name, l.color, l.position
    FROM label l
    WHERE l.book_id = OLD.series_id
    ON CONFLICT (book_id, name) DO NOTHING;

    INSERT INTO scene_label (scene_id, label_id)
    SELECT x.scene_id, bl.id
    FROM scene_label x
    JOIN scene s  ON s.id = x.scene_id AND s.book_id = NEW.id
    JOIN label sl ON sl.id = x.label_id AND sl.book_id = OLD.series_id
    JOIN label bl ON bl.book_id = NEW.id AND bl.name = sl.name
    ON CONFLICT DO NOTHING;

    DELETE FROM scene_label x
    USING scene s, label sl
    WHERE s.id = x.scene_id AND s.book_id = NEW.id
      AND sl.id = x.label_id AND sl.book_id = OLD.series_id;

    RETURN NULL;
END;
$$;

CREATE TRIGGER book_copy_down_series_scope
    AFTER UPDATE OF series_id ON book
    FOR EACH ROW
    WHEN (OLD.series_id IS NOT NULL AND OLD.series_id IS DISTINCT FROM NEW.series_id)
    EXECUTE FUNCTION book_copy_down_series_scope();

-- When a book joins (or switches to) a series: its own labels named like a
-- series label merge into the series one, and its own publication values
-- equal to the series' become inherited again. Same-named categories stay
-- two rows (the book's entries keep their scope) and book_kb_category shows
-- them as one. Fires after book_copy_down_series_scope on a switch.
CREATE FUNCTION book_join_series() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO scene_label (scene_id, label_id)
    SELECT x.scene_id, sl.id
    FROM scene_label x
    JOIN label bl ON bl.id = x.label_id AND bl.book_id = NEW.id
    JOIN label sl ON sl.book_id = NEW.series_id AND sl.name = bl.name
    ON CONFLICT DO NOTHING;

    DELETE FROM label bl
    USING label sl
    WHERE bl.book_id = NEW.id AND sl.book_id = NEW.series_id AND sl.name = bl.name;

    UPDATE publication_settings own SET
        citation_style   = nullif(own.citation_style, ser.citation_style),
        rules_name       = nullif(own.rules_name, ser.rules_name),
        quote_word_limit = nullif(own.quote_word_limit, ser.quote_word_limit),
        publisher_name   = nullif(own.publisher_name, ser.publisher_name),
        planned_year     = nullif(own.planned_year, ser.planned_year),
        print_run        = nullif(own.print_run, ser.print_run),
        formats          = nullif(own.formats, ser.formats),
        territory        = nullif(own.territory, ser.territory)
    FROM publication_settings ser
    WHERE own.book_id = NEW.id AND ser.book_id = NEW.series_id;

    RETURN NULL;
END;
$$;

CREATE TRIGGER book_join_series
    AFTER UPDATE OF series_id ON book
    FOR EACH ROW
    WHEN (NEW.series_id IS NOT NULL AND OLD.series_id IS DISTINCT FROM NEW.series_id)
    EXECUTE FUNCTION book_join_series();

-- ---------------------------------------------------------------------------
-- Duplicate book (Book settings → Archive & export)
-- ---------------------------------------------------------------------------

-- A new book with this one's acts, chapters, scenes, prose, beats, figures,
-- knowledge base, labels, publication and layout settings. References aren't
-- copied (clearance covers one book's print run), so source links are
-- unwrapped to their words. A series book stays in the series as its last
-- book. Archived rows come along so Restore still works in the copy. Returns
-- the new book's id.
CREATE FUNCTION duplicate_book(p_source uuid, p_title text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    v_new  uuid;
    v_map  jsonb := '{}';
BEGIN
    INSERT INTO book (kind, owner_id, series_id, series_number, title, subtitle, description,
                      author_name, cover_asset_id, cover_palette, status, target_word_count,
                      style_guide, copied_from_id)
    SELECT 'book', b.owner_id, b.series_id,
           CASE WHEN b.series_id IS NOT NULL THEN
               (SELECT max(series_number) + 1 FROM book WHERE series_id = b.series_id) END,
           p_title, b.subtitle, b.description, b.author_name, NULL, b.cover_palette,
           'planning', b.target_word_count, b.style_guide, b.id
    FROM book b WHERE b.id = p_source AND b.kind = 'book'
    RETURNING id INTO v_new;
    IF v_new IS NULL THEN
        RAISE EXCEPTION 'book % not found', p_source;
    END IF;
    DELETE FROM act WHERE book_id = v_new;  -- the default act; act_keep_one is deferred

    -- id map for every copied row; uuids are unique, so one map serves all text
    CREATE TEMP TABLE IF NOT EXISTS dup_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    TRUNCATE dup_map;
    INSERT INTO dup_map
    SELECT id, uuidv7() FROM act WHERE book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM chapter WHERE book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM scene WHERE book_id = p_source
    UNION ALL SELECT b.id, uuidv7() FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = p_source
    UNION ALL SELECT f.id, uuidv7() FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = p_source
    UNION ALL SELECT k.id, uuidv7() FROM kb_progression k JOIN scene s ON s.id = k.scene_id WHERE s.book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM kb_category WHERE book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM kb_entry WHERE book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM label WHERE book_id = p_source
    UNION ALL SELECT id, uuidv7() FROM asset WHERE book_id = p_source;
    SELECT jsonb_object_agg(old_id::text, new_id::text) INTO v_map FROM dup_map;
    v_map := coalesce(v_map, '{}');

    -- the book's own assets (same stored files); series assets are shared
    INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, width_px, height_px,
                       storage_key, meta, copied_from_id, created_at, archived_at)
    SELECT m.new_id, a.owner_id, v_new, a.kind, a.filename, a.mime, a.bytes, a.width_px, a.height_px,
           a.storage_key, a.meta, a.id, a.created_at, a.archived_at
    FROM asset a JOIN dup_map m ON m.old_id = a.id;
    INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key, created_at)
    SELECT m.new_id, r.purpose, r.mime, r.width_px, r.height_px, r.bytes, r.storage_key, r.created_at
    FROM asset_rendition r JOIN dup_map m ON m.old_id = r.asset_id;
    UPDATE book SET cover_asset_id = (
        SELECT coalesce(m.new_id, b.cover_asset_id) FROM book b
        LEFT JOIN dup_map m ON m.old_id = b.cover_asset_id WHERE b.id = p_source)
    WHERE id = v_new;

    INSERT INTO kb_category (id, book_id, name, tag, color, icon, position, field_defs)
    SELECT m.new_id, v_new, c.name, c.tag, c.color, c.icon, c.position, c.field_defs
    FROM kb_category c JOIN dup_map m ON m.old_id = c.id;

    INSERT INTO kb_entry (id, book_id, category_id, name, slug, position, aliases, tags, summary, body,
                          fields, custom_fields, always_in_context, exclude_from_auto_context,
                          import_url, import_archive_url, imported_at, copied_from_id, external_id,
                          archived_at)
    SELECT m.new_id, v_new, mc.new_id, e.name, e.slug, e.position, e.aliases, e.tags,
           remap_uuids(e.summary, v_map), remap_uuids(e.body, v_map), e.fields, e.custom_fields,
           e.always_in_context, e.exclude_from_auto_context, e.import_url, e.import_archive_url,
           e.imported_at, e.id, e.external_id, e.archived_at
    FROM kb_entry e
    JOIN dup_map m  ON m.old_id = e.id
    JOIN dup_map mc ON mc.old_id = e.category_id;

    -- own↔own pairs map both ends; own↔series pairs keep the series end
    INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
    SELECT least(a2, b2), greatest(a2, b2)
    FROM (SELECT coalesce(ma.new_id, l.entry_a_id) AS a2, coalesce(mb.new_id, l.entry_b_id) AS b2
          FROM kb_entry_link l
          LEFT JOIN dup_map ma ON ma.old_id = l.entry_a_id
          LEFT JOIN dup_map mb ON mb.old_id = l.entry_b_id
          WHERE ma.new_id IS NOT NULL OR mb.new_id IS NOT NULL) x
    ON CONFLICT DO NOTHING;

    INSERT INTO label (id, book_id, name, color, position)
    SELECT m.new_id, v_new, l.name, l.color, l.position
    FROM label l JOIN dup_map m ON m.old_id = l.id;

    INSERT INTO act (id, book_id, position, title, summary, opener_asset_id, opener_caption, archived_at)
    SELECT m.new_id, v_new, a.position, a.title, remap_uuids(a.summary, v_map),
           coalesce((v_map ->> a.opener_asset_id::text)::uuid, a.opener_asset_id), a.opener_caption,
           a.archived_at
    FROM act a JOIN dup_map m ON m.old_id = a.id;

    INSERT INTO chapter (id, book_id, act_id, position, title, slug, summary, notes, is_numbered,
                         opener_asset_id, opener_caption, archived_at)
    SELECT m.new_id, v_new, ma.new_id, c.position, c.title, c.slug, remap_uuids(c.summary, v_map),
           remap_uuids(c.notes, v_map), c.is_numbered,
           coalesce((v_map ->> c.opener_asset_id::text)::uuid, c.opener_asset_id), c.opener_caption,
           c.archived_at
    FROM chapter c JOIN dup_map m ON m.old_id = c.id JOIN dup_map ma ON ma.old_id = c.act_id;

    INSERT INTO scene (id, book_id, chapter_id, position, title, summary, plan_notes, status,
                       import_ref, archived_at)
    SELECT m.new_id, v_new, mc.new_id, s.position, s.title, remap_uuids(s.summary, v_map),
           remap_uuids(s.plan_notes, v_map), s.status, s.import_ref, s.archived_at
    FROM scene s JOIN dup_map m ON m.old_id = s.id JOIN dup_map mc ON mc.old_id = s.chapter_id;

    INSERT INTO beat (id, scene_id, body, target_word_count, is_hidden, model, archived_at)
    SELECT m.new_id, ms.new_id, remap_uuids(b.body, v_map), b.target_word_count, b.is_hidden,
           b.model, b.archived_at
    FROM beat b JOIN dup_map m ON m.old_id = b.id JOIN dup_map ms ON ms.old_id = b.scene_id;

    INSERT INTO figure (id, scene_id, asset_id, placement, caption, alt_text, archived_at)
    SELECT m.new_id, ms.new_id, coalesce((v_map ->> f.asset_id::text)::uuid, f.asset_id),
           f.placement, f.caption, f.alt_text, f.archived_at
    FROM figure f JOIN dup_map m ON m.old_id = f.id JOIN dup_map ms ON ms.old_id = f.scene_id;

    INSERT INTO kb_progression (id, kb_entry_id, scene_id, body, archived_at)
    SELECT m.new_id, coalesce(me.new_id, k.kb_entry_id), ms.new_id, remap_uuids(k.body, v_map),
           k.archived_at
    FROM kb_progression k
    JOIN dup_map m  ON m.old_id = k.id
    JOIN dup_map ms ON ms.old_id = k.scene_id
    LEFT JOIN dup_map me ON me.old_id = k.kb_entry_id;

    INSERT INTO scene_kb (scene_id, kb_entry_id)
    SELECT ms.new_id, coalesce(me.new_id, x.kb_entry_id)
    FROM scene_kb x JOIN dup_map ms ON ms.old_id = x.scene_id LEFT JOIN dup_map me ON me.old_id = x.kb_entry_id;

    INSERT INTO beat_kb (beat_id, kb_entry_id)
    SELECT mb.new_id, coalesce(me.new_id, x.kb_entry_id)
    FROM beat_kb x JOIN dup_map mb ON mb.old_id = x.beat_id LEFT JOIN dup_map me ON me.old_id = x.kb_entry_id;

    INSERT INTO scene_label (scene_id, label_id)
    SELECT ms.new_id, coalesce(ml.new_id, x.label_id)
    FROM scene_label x JOIN dup_map ms ON ms.old_id = x.scene_id LEFT JOIN dup_map ml ON ml.old_id = x.label_id;

    -- prose last, so every embedded row exists; source links become plain words
    INSERT INTO prose (scene_id, body)
    SELECT ms.new_id,
           remap_uuids(regexp_replace(p.body,
               '\[((?:[^\[\]\\]|\\.)*)\]\(ref:[0-9a-fA-F-]{36}\)', '\1', 'g'), v_map)
    FROM prose p JOIN dup_map ms ON ms.old_id = p.scene_id;

    INSERT INTO publication_settings (book_id, citation_style, rules_name, quote_word_limit,
                                      publisher_name, planned_year, print_run, formats, territory)
    SELECT v_new, citation_style, rules_name, quote_word_limit, publisher_name, planned_year,
           print_run, formats, territory
    FROM publication_settings WHERE book_id = p_source;

    INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size, options)
    SELECT v_new, template_id, template_version_id, trim_size, options
    FROM book_layout_template WHERE book_id = p_source;
    INSERT INTO book_layout (book_id, template_id)
    SELECT v_new, template_id FROM book_layout WHERE book_id = p_source;

    RETURN v_new;
END;
$$;
