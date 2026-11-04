-- L78c: Deleting a Knowledge base category is a soft delete (no hard deletes in app code). A deleted category keeps its
-- row with archived_at set, and its archived entries keep pointing at it so their own undo still works.
-- A deleted category's name can be used again: the (book_id, name) uniqueness now covers live categories only.
ALTER TABLE kb_category ADD COLUMN archived_at timestamptz;
ALTER TABLE kb_category DROP CONSTRAINT kb_category_name_uq;
CREATE UNIQUE INDEX kb_category_name_uq ON kb_category (book_id, name) WHERE archived_at IS NULL;

-- The sidebar's categories: live ones only.
CREATE OR REPLACE VIEW book_kb_category AS
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
JOIN kb_category c ON c.book_id = bs.scope_id AND c.archived_at IS NULL
GROUP BY bs.book_id, c.name;

-- Leaving a series copies down its live categories (and the live entries in them) only.
CREATE OR REPLACE FUNCTION book_copy_down_series_scope() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_map  jsonb;
BEGIN
    INSERT INTO kb_category (book_id, name, tag, color, icon, position, field_defs)
    SELECT NEW.id, c.name, c.tag, c.color, c.icon, c.position, c.field_defs
    FROM kb_category c
    WHERE c.book_id = OLD.series_id AND c.archived_at IS NULL
    ON CONFLICT (book_id, name) WHERE archived_at IS NULL DO NOTHING;

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
    JOIN kb_category sc ON sc.id = e.category_id AND sc.archived_at IS NULL
    JOIN kb_category bc ON bc.book_id = NEW.id AND bc.name = sc.name AND bc.archived_at IS NULL
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

-- Duplicating a book copies every category with its archived_at, so a deleted category stays deleted in the copy while
-- its archived entries still have a category to point at.
CREATE OR REPLACE FUNCTION duplicate_book(p_source uuid, p_title text) RETURNS uuid
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

    INSERT INTO kb_category (id, book_id, name, tag, color, icon, position, field_defs, archived_at)
    SELECT m.new_id, v_new, c.name, c.tag, c.color, c.icon, c.position, c.field_defs, c.archived_at
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
