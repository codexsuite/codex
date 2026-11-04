-- Rev 6 seed-and-assert checks. Each check writes one row to result.
CREATE TEMP TABLE result (n serial, name text, pass boolean, detail text);
CREATE FUNCTION ok(p_name text, p_pass boolean, p_detail text DEFAULT NULL) RETURNS void
LANGUAGE sql AS $$ INSERT INTO result (name, pass, detail) VALUES (p_name, coalesce(p_pass, false), p_detail) $$;
-- run sql, expect an error whose message matches pattern
CREATE FUNCTION fails(p_name text, p_sql text, p_pattern text) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE v_msg text;
BEGIN
    BEGIN
        EXECUTE p_sql;
        SET CONSTRAINTS ALL IMMEDIATE;
        RAISE EXCEPTION 'no error';  -- roll the statement back either way
    EXCEPTION WHEN OTHERS THEN
        v_msg := SQLERRM;
    END;
    SET CONSTRAINTS ALL DEFERRED;
    PERFORM ok(p_name, v_msg <> 'no error' AND v_msg ~* p_pattern, v_msg);
END;
$$;

-- ---------- seed ----------
INSERT INTO app_user (id, email, display_name) VALUES ('00000000-0000-7000-8000-000000000001', 'a@x', 'A');
INSERT INTO book (id, kind, owner_id, title) VALUES
    ('00000000-0000-7000-8000-0000000000f1', 'series', '00000000-0000-7000-8000-000000000001', 'Forsaken Gods'),
    ('00000000-0000-7000-8000-0000000000f2', 'series', '00000000-0000-7000-8000-000000000001', 'Ashen Crown');
INSERT INTO book (id, owner_id, series_id, series_number, title) VALUES
    ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000f1', 1, 'Players Guide'),
    ('00000000-0000-7000-8000-0000000000b2', '00000000-0000-7000-8000-000000000001', NULL, NULL, 'Product Engineering');

SELECT ok('default act is untitled',
    (SELECT title = '' FROM act WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));

-- book 1 structure
INSERT INTO chapter (id, book_id, act_id, position, title)
SELECT '00000000-0000-7000-8000-0000000000c1', book_id, id, 'a0', 'Shipping Small' FROM act WHERE book_id = '00000000-0000-7000-8000-0000000000b1';
INSERT INTO chapter (id, book_id, act_id, position, title)
SELECT '00000000-0000-7000-8000-0000000000c2', book_id, id, 'a1', 'Shipping Small' FROM act WHERE book_id = '00000000-0000-7000-8000-0000000000b1';
SELECT ok('chapter slug from title, deduped',
    (SELECT array_agg(slug ORDER BY position) = '{shipping-small,shipping-small-2}' FROM chapter
     WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
INSERT INTO scene (id, book_id, chapter_id, position, title) VALUES
    ('00000000-0000-7000-8000-0000000005a1', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000c1', 'a0', 'S1'),
    ('00000000-0000-7000-8000-0000000005a2', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000c1', 'a1', 'S2'),
    ('00000000-0000-7000-8000-0000000005a3', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000c2', 'a0', 'S3');

-- knowledge base: series category + entries, book's own category
INSERT INTO kb_category (id, book_id, name, position, field_defs) VALUES
    ('00000000-0000-7000-8000-00000000ca01', '00000000-0000-7000-8000-0000000000f1', 'Spells', 'a0', '[{"key":"level","label":"Level"}]'),
    ('00000000-0000-7000-8000-00000000ca02', '00000000-0000-7000-8000-0000000000b1', 'Spells', 'a0', '[]'),
    ('00000000-0000-7000-8000-00000000ca03', '00000000-0000-7000-8000-0000000000b1', 'Characters', 'a1', '[]');
INSERT INTO kb_entry (id, book_id, category_id, name, position, fields, custom_fields) VALUES
    ('00000000-0000-7000-8000-00000000e001', '00000000-0000-7000-8000-0000000000f1', '00000000-0000-7000-8000-00000000ca01', 'Fireball', 'a0', '{"level":"3"}', '[{"label":"URL","value":"https://x"}]'),
    ('00000000-0000-7000-8000-00000000e002', '00000000-0000-7000-8000-0000000000f1', '00000000-0000-7000-8000-00000000ca01', 'Goodhart''s Law', 'a1', '{}', '[]'),
    ('00000000-0000-7000-8000-00000000e003', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-00000000ca03', 'Maren', 'a0', '{}', '[]'),
    ('00000000-0000-7000-8000-00000000e004', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-00000000ca03', 'Fireball', 'a1', '{}', '[]');
SELECT ok('entry slug from name strips apostrophes',
    (SELECT slug = 'goodharts-law' FROM kb_entry WHERE id = '00000000-0000-7000-8000-00000000e002'));
UPDATE kb_entry SET name = 'Goodhart''s Law (metric)' WHERE id = '00000000-0000-7000-8000-00000000e002';
SELECT ok('rename keeps slug', (SELECT slug = 'goodharts-law' FROM kb_entry WHERE id = '00000000-0000-7000-8000-00000000e002'));
SELECT fails('slug never changes',
    $$UPDATE kb_entry SET slug = 'x' WHERE id = '00000000-0000-7000-8000-00000000e002'$$, 'never changes');
SELECT ok('book_kb_category merges same-named categories',
    (SELECT count(*) = 2 AND bool_and(name <> 'Spells' OR cardinality(category_ids) = 2)
     FROM book_kb_category WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));

-- related links
INSERT INTO kb_entry_link (entry_a_id, entry_b_id) VALUES
    ('00000000-0000-7000-8000-00000000e001', '00000000-0000-7000-8000-00000000e002'),  -- series↔series
    ('00000000-0000-7000-8000-00000000e002', '00000000-0000-7000-8000-00000000e003');  -- series↔book
SELECT ok('related reads both ways',
    (SELECT count(*) = 2 FROM kb_entry_related WHERE kb_entry_id = '00000000-0000-7000-8000-00000000e002'));
SELECT fails('related pair stored smaller id first',
    $$INSERT INTO kb_entry_link VALUES ('00000000-0000-7000-8000-00000000e003', '00000000-0000-7000-8000-00000000e001')$$, 'kb_entry_link_ordered');
INSERT INTO kb_category (id, book_id, name, position) VALUES ('00000000-0000-7000-8000-00000000ca09', '00000000-0000-7000-8000-0000000000b2', 'People', 'a0');
INSERT INTO kb_entry (id, book_id, category_id, name, position) VALUES ('00000000-0000-7000-8000-00000000e009', '00000000-0000-7000-8000-0000000000b2', '00000000-0000-7000-8000-00000000ca09', 'Kent Beck', 'a0');
SELECT fails('related across unrelated books rejected',
    $$INSERT INTO kb_entry_link VALUES ('00000000-0000-7000-8000-00000000e003', '00000000-0000-7000-8000-00000000e009')$$, 'outside the scope');

-- assets and figures
INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, width_px, height_px, storage_key) VALUES
    ('00000000-0000-7000-8000-0000000a5e01', '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000f1', 'image', 'dragon.webp', 'image/webp', 100, 1792, 2560, 's/1'),
    ('00000000-0000-7000-8000-0000000a5e02', '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000b2', 'image', 'other.png', 'image/png', 100, 100, 100, 's/2'),
    ('00000000-0000-7000-8000-0000000a5e03', '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000b1', 'image', 'opener.png', 'image/png', 100, 100, 100, 's/3');
INSERT INTO asset_rendition VALUES ('00000000-0000-7000-8000-0000000a5e01', 'proxy', 'image/jpeg', 600, 800, 10, 's/1p', now());
INSERT INTO figure (id, scene_id, asset_id, placement, caption) VALUES
    ('00000000-0000-7000-8000-00000000f101', '00000000-0000-7000-8000-0000000005a1', '00000000-0000-7000-8000-0000000a5e01', 'page', 'The dragon');
SELECT fails('figure asset from another book rejected',
    $$INSERT INTO figure (scene_id, asset_id) VALUES ('00000000-0000-7000-8000-0000000005a1', '00000000-0000-7000-8000-0000000a5e02')$$, 'outside the scope');
UPDATE chapter SET opener_asset_id = '00000000-0000-7000-8000-0000000a5e03' WHERE id = '00000000-0000-7000-8000-0000000000c1';
SELECT fails('used asset cannot be hard-deleted',
    $$DELETE FROM asset WHERE id = '00000000-0000-7000-8000-0000000a5e01'$$, 'foreign key');

-- beats, timeline notes, prose with every embed kind
INSERT INTO beat (id, scene_id, body) VALUES
    ('00000000-0000-7000-8000-0000000be001', '00000000-0000-7000-8000-0000000005a1', 'Introduce [Fireball](kb:00000000-0000-7000-8000-00000000e001)'),
    ('00000000-0000-7000-8000-0000000be002', '00000000-0000-7000-8000-0000000005a1', 'Second beat');
INSERT INTO beat_kb VALUES ('00000000-0000-7000-8000-0000000be002', '00000000-0000-7000-8000-00000000e002');
INSERT INTO scene_kb VALUES ('00000000-0000-7000-8000-0000000005a2', '00000000-0000-7000-8000-00000000e001');
INSERT INTO kb_progression (id, kb_entry_id, scene_id, body) VALUES
    ('00000000-0000-7000-8000-0000000d0001', '00000000-0000-7000-8000-00000000e001', '00000000-0000-7000-8000-0000000005a3', 'learned later'),
    ('00000000-0000-7000-8000-0000000d0002', '00000000-0000-7000-8000-00000000e001', '00000000-0000-7000-8000-0000000005a1', 'learned first');
INSERT INTO prose (scene_id, body) VALUES
    ('00000000-0000-7000-8000-0000000005a1', E'::beat{id="00000000-0000-7000-8000-0000000be001"}\n::beat{id="00000000-0000-7000-8000-0000000be002"}\n\nThe [fire](kb:00000000-0000-7000-8000-00000000e001) roared.\n\n::kb-addition{id="00000000-0000-7000-8000-0000000d0002"}\n\n::figure{id="00000000-0000-7000-8000-00000000f101"}\n\n::spell{kb="00000000-0000-7000-8000-00000000e001"}\n::spell{kb="00000000-0000-7000-8000-00000000e001"}\n\n:::sidebar{title="Rules"}\nRoll two dice.\n:::\n\n::pagebreak\n\n```{=latex}\n\\DndDropCapLine{F}ighters fight\n```\nEnd.'),
    ('00000000-0000-7000-8000-0000000005a3', E'::kb-addition{id="00000000-0000-7000-8000-0000000d0001"}\n\nLater text here.');
SELECT ok('word count skips directives, containers fences and raw LaTeX',
    (SELECT word_count = 7 FROM prose WHERE scene_id = '00000000-0000-7000-8000-0000000005a1'),
    (SELECT word_count::text FROM prose WHERE scene_id = '00000000-0000-7000-8000-0000000005a1'));
SELECT ok('repeated ::spell allowed', true);
SELECT fails('::spell outside scope rejected',
    $$UPDATE prose SET body = body || E'\n::spell{kb="00000000-0000-7000-8000-00000000e009"}' WHERE scene_id = '00000000-0000-7000-8000-0000000005a1'$$, 'outside the scope');
SELECT fails('::figure from another scene rejected',
    $$UPDATE prose SET body = E'::figure{id="00000000-0000-7000-8000-00000000f101"}' WHERE scene_id = '00000000-0000-7000-8000-0000000005a3'$$, 'does not belong');
SELECT ok('timeline in book order',
    (SELECT array_agg(progression_id ORDER BY timeline_order) = '{00000000-0000-7000-8000-0000000d0002,00000000-0000-7000-8000-0000000d0001}'
     FROM kb_progression_outline WHERE kb_entry_id = '00000000-0000-7000-8000-00000000e001'));
SELECT ok('used-in kinds',
    (SELECT array_agg(DISTINCT kind ORDER BY kind) = '{beat,linked,mention,printed,timeline}'
     FROM kb_entry_use WHERE kb_entry_id = '00000000-0000-7000-8000-00000000e001'),
    (SELECT string_agg(DISTINCT kind, ',') FROM kb_entry_use WHERE kb_entry_id = '00000000-0000-7000-8000-00000000e001'));
SELECT ok('beat context counted',
    (SELECT count(*) = 1 FROM kb_entry_use WHERE kb_entry_id = '00000000-0000-7000-8000-00000000e002' AND kind = 'beat_context'));
SELECT fails('entry with a note in the prose cannot be hard-deleted',
    $$DELETE FROM kb_entry WHERE id = '00000000-0000-7000-8000-00000000e001'$$, 'timeline notes');
UPDATE prose SET body = replace(body, E'::figure{id="00000000-0000-7000-8000-00000000f101"}', '') WHERE scene_id = '00000000-0000-7000-8000-0000000005a1';
SELECT ok('removing ::figure line archives the figure',
    (SELECT archived_at IS NOT NULL FROM figure WHERE id = '00000000-0000-7000-8000-00000000f101'));
UPDATE prose SET body = body || E'\n::figure{id="00000000-0000-7000-8000-00000000f101"}' WHERE scene_id = '00000000-0000-7000-8000-0000000005a1';
SELECT ok('putting it back restores it',
    (SELECT archived_at IS NULL FROM figure WHERE id = '00000000-0000-7000-8000-00000000f101'));

-- positions: archived scene keeps its key, a new live scene may take it
UPDATE scene SET archived_at = now() - interval '2 hours' WHERE id = '00000000-0000-7000-8000-0000000005a2';
INSERT INTO scene (id, book_id, chapter_id, position, title) VALUES
    ('00000000-0000-7000-8000-0000000005a4', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000c1', 'a1', 'S2 replacement');
SELECT ok('new scene takes an archived scene''s position', true);
SELECT fails('two live scenes can''t share a position',
    $$INSERT INTO scene (book_id, chapter_id, position) VALUES ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000c1', 'a1')$$, 'scene_position_uq');
UPDATE scene SET position = 'a2' WHERE id = '00000000-0000-7000-8000-0000000005a4';

-- references: archived-scene quote flag, pending draft markers, restore reopens sign-off
INSERT INTO reference (id, book_id, kind, title, authors, year, publisher, licence) VALUES
    ('00000000-0000-7000-8000-0000000ef001', '00000000-0000-7000-8000-0000000000b1', 'book', 'Accelerate', '[{"family":"Forsgren"}]', 2018, 'IT Rev', 'all_rights_reserved');
INSERT INTO reference_use (id, book_id, reference_id, scene_id, kind, changed_at) VALUES
    ('00000000-0000-7000-8000-00000000a501', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000ef001', '00000000-0000-7000-8000-0000000005a2', 'paraphrase', now() - interval '3 hours'),
    ('00000000-0000-7000-8000-00000000a502', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000ef001', '00000000-0000-7000-8000-0000000005a3', 'paraphrase', now() - interval '3 hours');
INSERT INTO prose (scene_id, body) VALUES ('00000000-0000-7000-8000-0000000005a2', '[speed matters](ref:00000000-0000-7000-8000-00000000a501)');
UPDATE prose SET body = body || ' [deploy often](ref:00000000-0000-7000-8000-00000000a502)' WHERE scene_id = '00000000-0000-7000-8000-0000000005a3';
INSERT INTO quote_flag (book_id, scene_id, excerpt, reason) VALUES
    ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000005a2', '"quoted in an archived scene"', 'quotation_marks');
SELECT ok('open quote flags skip archived scenes',
    (SELECT open_quote_flags = 0 FROM book_reference_summary WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
-- linking the words just bumped changed_at; pretend that was before sign-off
UPDATE reference_use SET changed_at = now() - interval '3 hours' WHERE reference_id = '00000000-0000-7000-8000-0000000ef001';
UPDATE reference SET signed_off_at = now() - interval '1 hour' WHERE id = '00000000-0000-7000-8000-0000000ef001';
SELECT ok('signed off before restore: cleared',
    (SELECT status = 'cleared' FROM reference_clearance WHERE reference_id = '00000000-0000-7000-8000-0000000ef001'),
    (SELECT status || '/' || reason FROM reference_clearance WHERE reference_id = '00000000-0000-7000-8000-0000000ef001'));
UPDATE scene SET archived_at = NULL, position = 'a1' WHERE id = '00000000-0000-7000-8000-0000000005a2';
SELECT ok('restoring a scene reopens sign-off',
    (SELECT reason = 'new_uses_since_signoff' FROM reference_clearance WHERE reference_id = '00000000-0000-7000-8000-0000000ef001'),
    (SELECT status || '/' || reason FROM reference_clearance WHERE reference_id = '00000000-0000-7000-8000-0000000ef001'));

-- pending AI draft in scene a1 (before scene a3's use in book order)
INSERT INTO ai_generation (id, book_id, scene_id, beat_id, kind, model, request, output, status) VALUES
    ('00000000-0000-7000-8000-00000000a1d1', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000005a1', '00000000-0000-7000-8000-0000000be002',
     'beat_prose', 'm', '{}', 'Drafted [as Goodhart warned](ref:00000000-0000-7000-8000-00000000a503).', 'pending');
INSERT INTO reference (id, book_id, kind, title, licence) VALUES
    ('00000000-0000-7000-8000-0000000ef002', '00000000-0000-7000-8000-0000000000b1', 'web', 'Goodhart', 'cc_by_4');
INSERT INTO reference_use (id, book_id, reference_id, scene_id, generation_id, excerpt) VALUES
    ('00000000-0000-7000-8000-00000000a503', '00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000ef002', '00000000-0000-7000-8000-0000000005a1', '00000000-0000-7000-8000-00000000a1d1', 'as Goodhart warned');
UPDATE prose SET body = body || E'\nMore.' WHERE scene_id = '00000000-0000-7000-8000-0000000005a1';
SELECT ok('prose save keeps a pending draft''s use',
    (SELECT archived_at IS NULL FROM reference_use WHERE id = '00000000-0000-7000-8000-00000000a503'));
SELECT ok('pending draft marker gets the first number',
    (SELECT number = 1 FROM reference_stats WHERE reference_id = '00000000-0000-7000-8000-0000000ef002'),
    (SELECT string_agg(reference_id::text || '=' || number, ' ') FROM reference_stats));
UPDATE ai_generation SET status = 'discarded' WHERE id = '00000000-0000-7000-8000-00000000a1d1';
SELECT ok('discarding the draft archives its use',
    (SELECT archived_at IS NOT NULL AND generation_id IS NULL FROM reference_use WHERE id = '00000000-0000-7000-8000-00000000a503'));
SELECT ok('and the source is not cited again',
    (SELECT status = 'not_cited' FROM reference_clearance WHERE reference_id = '00000000-0000-7000-8000-0000000ef002'));

-- archived chapter drops out of totals
SELECT ok('book words before archiving chapter', (SELECT word_count > 0 FROM book_stats WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
CREATE TEMP TABLE before AS SELECT word_count FROM book_stats WHERE book_id = '00000000-0000-7000-8000-0000000000b1';
UPDATE chapter SET archived_at = now() WHERE id = '00000000-0000-7000-8000-0000000000c2';
SELECT ok('scenes in an archived chapter leave scene_stats and book_stats',
    (SELECT NOT EXISTS (SELECT 1 FROM scene_stats WHERE scene_id = '00000000-0000-7000-8000-0000000005a3'))
    AND (SELECT s.word_count < b.word_count FROM book_stats s, before b WHERE s.book_id = '00000000-0000-7000-8000-0000000000b1'));
UPDATE chapter SET archived_at = NULL WHERE id = '00000000-0000-7000-8000-0000000000c2';

-- publication settings
SELECT fails('formats limited to known values',
    $$INSERT INTO publication_settings (book_id, formats) VALUES ('00000000-0000-7000-8000-0000000000b2', '{print,vinyl}')$$, 'formats');
INSERT INTO publication_settings (book_id, citation_style, quote_word_limit, formats, publisher_name) VALUES
    ('00000000-0000-7000-8000-0000000000f2', 'apa', 250, '{print,pdf}', 'Ashen Press'),
    ('00000000-0000-7000-8000-0000000000b2', 'apa', 300, '{print,pdf}', '');
SELECT fails('series book needs a number',
    $$UPDATE book SET series_id = '00000000-0000-7000-8000-0000000000f2' WHERE id = '00000000-0000-7000-8000-0000000000b2'$$, 'series_number');

-- layout
INSERT INTO layout_template (id, slug, name) VALUES ('00000000-0000-7000-8000-00000000a7e1', 'fantasy-rpg', 'Fantasy RPG');
SELECT fails('built-in template slugs unique (NULL owner)',
    $$INSERT INTO layout_template (slug, name) VALUES ('fantasy-rpg', 'dup')$$, 'layout_template_slug_uq');
INSERT INTO layout_template_version (id, template_id, major, minor, patch, engine, manifest, published_at) VALUES
    ('00000000-0000-7000-8000-00000000a7f1', '00000000-0000-7000-8000-00000000a7e1', 1, 4, 0, 'lualatex', '{"trims":["letter","a4"],"files":{"preview.css":{}}}', now()),
    ('00000000-0000-7000-8000-00000000a7f2', '00000000-0000-7000-8000-00000000a7e1', 1, 5, 0, 'lualatex', '{"trims":["letter"],"files":{}}', now());
SELECT fails('trim must be one the version supports',
    $$INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size) VALUES ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-00000000a7e1', '00000000-0000-7000-8000-00000000a7f1', '6x9')$$, 'not supported');
SELECT fails('layout only on real books',
    $$INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size) VALUES ('00000000-0000-7000-8000-0000000000f1', '00000000-0000-7000-8000-00000000a7e1', '00000000-0000-7000-8000-00000000a7f1', 'a4')$$, 'book_layout_template_book_fk');
INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size, options) VALUES
    ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-00000000a7e1', '00000000-0000-7000-8000-00000000a7f1', 'a4', '{"columns":2}');
INSERT INTO book_layout (book_id, template_id) VALUES ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-00000000a7e1');
SELECT ok('book_layout_current shows pinned 1.4.0, upgrade 1.5.0',
    (SELECT pinned_version = '1.4.0' AND latest_version = '1.5.0' AND upgrade_available AND has_preview_css
     FROM book_layout_current WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
SELECT fails('upgrading to a version without the trim is refused',
    $$UPDATE book_layout_template SET template_version_id = '00000000-0000-7000-8000-00000000a7f2' WHERE book_id = '00000000-0000-7000-8000-0000000000b1'$$, 'not supported');

-- export jobs
SELECT fails('proof needs a chapter',
    $$INSERT INTO export_job (book_id, kind, template_version_id, trim_size) VALUES ('00000000-0000-7000-8000-0000000000b1', 'proof', '00000000-0000-7000-8000-00000000a7f1', 'a4')$$, 'export_job_proof_chapter_ck');
INSERT INTO export_job (book_id, kind, chapter_id, template_version_id, trim_size, options, content_hash) VALUES
    ('00000000-0000-7000-8000-0000000000b1', 'proof', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000a7f1', 'a4', '{"columns":2}', 'h1'),
    ('00000000-0000-7000-8000-0000000000b1', 'proof', '00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000a7f1', 'a4', '{"columns": 2}', 'h1');
SELECT ok('same inputs give the same cache key', (SELECT count(DISTINCT cache_key) = 1 FROM export_job WHERE kind = 'proof'));
INSERT INTO export_job (book_id, kind, template_version_id, trim_size) VALUES ('00000000-0000-7000-8000-0000000000b1', 'print', '00000000-0000-7000-8000-00000000a7f1', 'a4');
SELECT fails('one live print export per book',
    $$INSERT INTO export_job (book_id, kind, template_version_id, trim_size) VALUES ('00000000-0000-7000-8000-0000000000b1', 'print', '00000000-0000-7000-8000-00000000a7f1', 'a4')$$, 'export_job_live_print_uq');

-- last edited: force an old value, then edit
UPDATE book SET last_edited_at = now() - interval '1 day' WHERE id = '00000000-0000-7000-8000-0000000000b1';
UPDATE beat SET body = body || '!' WHERE id = '00000000-0000-7000-8000-0000000be002';
SELECT ok('a beat edit bumps book.last_edited_at',
    (SELECT last_edited_at > now() - interval '1 minute' FROM book WHERE id = '00000000-0000-7000-8000-0000000000b1'));

-- duplicate (while still in the series)
CREATE TEMP TABLE dup AS SELECT duplicate_book('00000000-0000-7000-8000-0000000000b1', 'Players Guide (copy)') AS id;
SELECT ok('duplicate joins the series as the last book',
    (SELECT series_number = 2 AND series_id = '00000000-0000-7000-8000-0000000000f1' FROM book WHERE id = (SELECT id FROM dup)));
SELECT ok('duplicate copies the manuscript',
    (SELECT (SELECT count(*) FROM act WHERE book_id = d.id) = 1
        AND (SELECT count(*) FROM chapter WHERE book_id = d.id) = 2
        AND (SELECT count(*) FROM scene WHERE book_id = d.id) = 4
        AND (SELECT count(*) FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = d.id) = 2
        AND (SELECT count(*) FROM kb_entry WHERE book_id = d.id) = 2
        AND (SELECT count(*) FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = d.id) = 1
        AND (SELECT count(*) FROM book_layout WHERE book_id = d.id) = 1
     FROM dup d));
SELECT ok('duplicate has no references and unwraps source links',
    (SELECT NOT EXISTS (SELECT 1 FROM reference WHERE book_id = d.id)
        AND NOT EXISTS (SELECT 1 FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = d.id AND p.body LIKE '%(ref:%')
        AND EXISTS (SELECT 1 FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = d.id AND p.body LIKE '%deploy often%')
     FROM dup d));
SELECT ok('duplicate beats live in the copy''s prose',
    (SELECT count(*) = 2 FROM beat_outline bo JOIN scene s ON s.id = bo.scene_id WHERE s.book_id = (SELECT id FROM dup)));
SELECT ok('duplicate copies the book''s opener asset',
    (SELECT a.book_id = d.id FROM chapter c JOIN asset a ON a.id = c.opener_asset_id, dup d
     WHERE c.book_id = d.id AND c.slug = 'shipping-small'));
SELECT ok('duplicate keeps series entry links (series entries are shared)',
    (SELECT count(*) = 1 FROM scene_kb x JOIN scene s ON s.id = x.scene_id
     WHERE s.book_id = (SELECT id FROM dup) AND x.kb_entry_id = '00000000-0000-7000-8000-00000000e001'));

-- leave the series: copy-down
UPDATE book SET series_id = NULL, series_number = NULL WHERE id = '00000000-0000-7000-8000-0000000000b1';
SELECT ok('copy-down reuses same-named category and copies its field defs onto new ones',
    (SELECT count(*) = 2 FROM kb_category WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
SELECT ok('copies keep slugs and fields; a clash gets a suffix',
    (SELECT array_agg(slug ORDER BY slug) = '{fireball,fireball-2,goodharts-law,maren}'
        AND bool_or(custom_fields = '[{"label":"URL","value":"https://x"}]')
     FROM kb_entry WHERE book_id = '00000000-0000-7000-8000-0000000000b1'),
    (SELECT string_agg(slug, ',' ORDER BY slug) FROM kb_entry WHERE book_id = '00000000-0000-7000-8000-0000000000b1'));
SELECT ok('mentions and ::spell repointed at copies',
    (SELECT body NOT LIKE '%00000000-0000-7000-8000-00000000e001%'
        AND body LIKE '%' || (SELECT id::text FROM kb_entry WHERE book_id = '00000000-0000-7000-8000-0000000000b1' AND copied_from_id = '00000000-0000-7000-8000-00000000e001') || '%'
     FROM prose WHERE scene_id = '00000000-0000-7000-8000-0000000005a1'));
SELECT ok('beat body mention repointed',
    (SELECT body NOT LIKE '%e001)%' FROM beat WHERE id = '00000000-0000-7000-8000-0000000be001'));
SELECT ok('related links copied and repointed',
    (SELECT count(*) = 2 FROM kb_entry_link l JOIN kb_entry a ON a.id = l.entry_a_id JOIN kb_entry b ON b.id = l.entry_b_id
     WHERE a.book_id = '00000000-0000-7000-8000-0000000000b1' AND b.book_id = '00000000-0000-7000-8000-0000000000b1'));
SELECT ok('series figure asset copied and repointed',
    (SELECT a.book_id = '00000000-0000-7000-8000-0000000000b1' AND a.copied_from_id = '00000000-0000-7000-8000-0000000a5e01'
        AND EXISTS (SELECT 1 FROM asset_rendition r WHERE r.asset_id = a.id)
     FROM figure f JOIN asset a ON a.id = f.asset_id WHERE f.id = '00000000-0000-7000-8000-00000000f101'));
SELECT ok('used-in still finds the copy',
    (SELECT count(DISTINCT kind) >= 4 FROM kb_entry_use u JOIN kb_entry e ON e.id = u.kb_entry_id
     WHERE e.copied_from_id = '00000000-0000-7000-8000-00000000e001' AND e.book_id = '00000000-0000-7000-8000-0000000000b1'));

-- join another series: labels merge, equal settings become inherited
INSERT INTO label (id, book_id, name, position) VALUES
    ('00000000-0000-7000-8000-0000000ab001', '00000000-0000-7000-8000-0000000000f2', 'Playtested', 'a0'),
    ('00000000-0000-7000-8000-0000000ab002', '00000000-0000-7000-8000-0000000000b2', 'Playtested', 'a0');
INSERT INTO act (book_id, position, title) SELECT '00000000-0000-7000-8000-0000000000b2', 'a1', 'x';
INSERT INTO chapter (id, book_id, act_id, position, title)
SELECT '00000000-0000-7000-8000-0000000000c9', book_id, id, 'a0', 'C' FROM act WHERE book_id = '00000000-0000-7000-8000-0000000000b2' AND position = 'a0';
INSERT INTO scene (id, book_id, chapter_id, position) VALUES ('00000000-0000-7000-8000-0000000005b1', '00000000-0000-7000-8000-0000000000b2', '00000000-0000-7000-8000-0000000000c9', 'a0');
INSERT INTO scene_label VALUES ('00000000-0000-7000-8000-0000000005b1', '00000000-0000-7000-8000-0000000ab002');
UPDATE book SET series_id = '00000000-0000-7000-8000-0000000000f2', series_number = 1 WHERE id = '00000000-0000-7000-8000-0000000000b2';
SELECT ok('same-named label merged into the series label',
    (SELECT (SELECT archived_at IS NOT NULL FROM label WHERE id = '00000000-0000-7000-8000-0000000ab002')
        AND EXISTS (SELECT 1 FROM scene_label WHERE scene_id = '00000000-0000-7000-8000-0000000005b1' AND label_id = '00000000-0000-7000-8000-0000000ab001')));
SELECT ok('own values equal to the series become inherited; others stay',
    (SELECT citation_style IS NULL AND formats IS NULL AND quote_word_limit = 300 AND publisher_name = ''
     FROM publication_settings WHERE book_id = '00000000-0000-7000-8000-0000000000b2'));
SELECT ok('blank publisher overrides the series',
    (SELECT publisher_name = '' AND citation_style = 'apa' FROM book_publication_settings WHERE book_id = '00000000-0000-7000-8000-0000000000b2'));

-- import record
SELECT fails('done import needs its book',
    $$INSERT INTO book_import (source, file_name, status) VALUES ('novelcrafter', 'pe.zip', 'done')$$, 'book_import_done_ck');

SELECT n, CASE WHEN pass THEN 'PASS' ELSE 'FAIL' END AS r, name, CASE WHEN NOT pass THEN detail END AS detail FROM result ORDER BY n;
SELECT count(*) FILTER (WHERE pass) || '/' || count(*) AS total FROM result;
