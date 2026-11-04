-- Iron Litany at full size: 3 acts, 30 chapters of 3 scenes (90 scenes), 124,870 words in total.
-- Replaces the seed's filler chapters for that one book. Scene lengths run 500 to 2,278 words by a fixed formula;
-- the last scene takes whatever makes the total exact. Every scene's word count equals its target (the lead
-- sentence counts toward it, as in db/seed/05_prose.sql). Deterministic: ids are md5 of fixed strings, filler walks a vocabulary.
-- Layout: scene k (1..90) is chapter (k-1)/3+1, scene (k-1)%3+1. Chapters 1-10 are act I, 11-20 act II, 21-30 act III.
-- Every 5th scene opens with a ::beat line; every 3rd scene's first sentence mentions an Ashen Crown series entry;
-- scenes 16, 31, 46, 61 and 76 open by linking to an earlier chapter.

CREATE FUNCTION pg_temp.filler(n integer, seed integer, psize integer DEFAULT 60) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    WITH vocab AS (
        SELECT ARRAY[
            'iron','ash','crown','litany','ember','oath','hollow','throne','cinder','bell',
            'prayer','salt','ledger','furnace','chain','vigil','ward','smoke','ridge','harbor',
            'lantern','verse','hymn','blade','mourner','ferry','gate','tower','bone','river',
            'candle','road','banner','siege','tithe','relic','shadow','winter','granite','song',
            'envoy','heir','scribe','stair','vault','wind','ruin','market','forge','pilgrim',
            'drum','torch','marsh','cloak','seal','summit','cellar','hearth','wolf','dawn',
            'knell','altar','crypt','steel'] AS w
    )
    SELECT coalesce(string_agg(
               CASE WHEN (i - 1) % 12 = 0 THEN initcap(word) ELSE word END
               || CASE WHEN i % 12 = 0 OR i = n THEN '.' ELSE '' END
               || CASE WHEN i = n THEN '' WHEN i % psize = 0 THEN E'\n\n' ELSE ' ' END,
               '' ORDER BY i), '')
    FROM (
        SELECT i, vocab.w[1 + ((i * 7 + i / 5 + seed * 13) % cardinality(vocab.w))] AS word
        FROM vocab, generate_series(1, n) AS i
    ) t
$$;

CREATE FUNCTION pg_temp.blocks(VARIADIC parts text[]) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    SELECT coalesce(string_agg(p, E'\n\n' ORDER BY ord) || E'\n', '')
    FROM unnest(parts) WITH ORDINALITY AS t (p, ord)
    WHERE p <> ''
$$;

CREATE FUNCTION pg_temp.lead_to(n integer, lead text, seed integer, psize integer DEFAULT 60) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    SELECT CASE WHEN n > markdown_word_count(lead)
                THEN lead || E'\n\n' || pg_temp.filler(n - markdown_word_count(lead), seed, psize)
                ELSE lead END
$$;

-- ---------------------------------------------------------------------------
-- Ashen Crown series entries to mention (the series categories exist; the normal seed gives them no entries).
-- ---------------------------------------------------------------------------
INSERT INTO kb_entry (id, book_id, category_id, name, position, aliases, summary, body)
VALUES
    (md5('il-kb:1')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000007'::uuid /*AshenCrownCharacters*/, 'Maren Voss',     'a0', '{}', 'The litanist of the iron choir.', 'Maren Voss keeps the Iron Litany and teaches it to those who will carry it.'),
    (md5('il-kb:2')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000007'::uuid /*AshenCrownCharacters*/, 'Orrin Hale',     'a1', '{}', 'A forge-captain loyal to the ember oath.', 'Orrin Hale commands the garrison at Cinder Gate.'),
    (md5('il-kb:3')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000007'::uuid /*AshenCrownCharacters*/, 'Sister Ysolde',  'a2', '{}', 'A pilgrim who carries the last copy of the litany.', 'Sister Ysolde walks the ash road between the harbor and the ridge.'),
    (md5('il-kb:4')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000008'::uuid /*AshenCrownPlaces*/,     'Cinder Gate',    'a0', '{}', 'The northern gate of the ash road.', 'Cinder Gate is where the ember oath was sworn and where the first bell was cast.'),
    (md5('il-kb:5')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000008'::uuid /*AshenCrownPlaces*/,     'The Iron Choir', 'a1', '{}', 'The order that sings the litany.', 'The Iron Choir meets in the crypt under the harbor tower.'),
    (md5('il-kb:6')::uuid, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/, '0199c0de-0000-7000-8000-000a00000008'::uuid /*AshenCrownPlaces*/,     'Hollow Ridge',   'a2', '{}', 'The ridge above the salt marsh.', 'Hollow Ridge holds the old beacon and the road to the throne.');

-- ---------------------------------------------------------------------------
-- Structure: replace the book's filler chapters. The seed's single act stays as act I.
-- ---------------------------------------------------------------------------
DELETE FROM chapter WHERE book_id = '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/;

UPDATE act SET title = 'Act I: The Bell' WHERE book_id = '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/;

INSERT INTO act (id, book_id, position, title)
VALUES
    (md5('il-act:2')::uuid, '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/, 'a1', 'Act II: The Chain'),
    (md5('il-act:3')::uuid, '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/, 'a2', 'Act III: The Last Verse');

INSERT INTO chapter (id, book_id, act_id, position, title)
SELECT md5('il-chapter:' || c)::uuid,
       '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/,
       CASE (c - 1) / 10 WHEN 0 THEN (SELECT id FROM act WHERE book_id = '0199c0de-0000-7000-8000-000300000005'::uuid AND position = 'a0')
                         WHEN 1 THEN md5('il-act:2')::uuid
                         ELSE md5('il-act:3')::uuid END,
       'a' || ((c - 1) % 10),
       (ARRAY['The Cold Bell','Ash on the Road','A Verse Unsung','The Harbor Tower','Salt and Iron','What the Choir Knew',
              'Cinder Gate','The Oath Reversed','Ledger of the Dead','The Long Vigil','Hollow Ridge','A Chain of Hymns',
              'The Pilgrim''s Toll','Smoke Over the Marsh','Winter Quarters','The Envoy''s Price','Bones Under the Altar','A Second Litany',
              'The Furnace Door','Night of Drums','The Throne Road','Granite and Glass','The Heir Unnamed','Candle Wars',
              'The Seventh Seal','Ferry to Nowhere','What the Wolf Heard','The Summit Stair','Dawn Without Bells','The Last Verse'])[c]
FROM generate_series(1, 30) AS c;

-- ---------------------------------------------------------------------------
-- Scenes, beats and prose. n is the scene's word target; the last scene absorbs the difference to 124,870.
-- ---------------------------------------------------------------------------
INSERT INTO scene (id, book_id, chapter_id, position, summary, status)
SELECT md5('il-scene:' || k)::uuid,
       '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/,
       md5('il-chapter:' || ((k - 1) / 3 + 1))::uuid,
       'a' || ((k - 1) % 3),
       'Chapter ' || ((k - 1) / 3 + 1) || ', scene ' || ((k - 1) % 3 + 1) || ' of the Iron Litany.',
       'draft'
FROM generate_series(1, 90) AS k;

INSERT INTO beat (id, scene_id, body, target_word_count)
SELECT md5('il-beat:' || k)::uuid, md5('il-scene:' || k)::uuid,
       'Carry the litany one verse further, and show what it costs.', 400
FROM generate_series(5, 90, 5) AS k;

INSERT INTO prose (scene_id, body, updated_by)
SELECT md5('il-scene:' || s.k)::uuid,
       pg_temp.blocks(
           CASE WHEN s.k % 5 = 0 THEN '::beat{id="' || md5('il-beat:' || s.k)::uuid || '"}' ELSE '' END,
           pg_temp.lead_to(s.n::integer,
               CASE
                   WHEN s.k IN (16, 31, 46, 61, 76) THEN
                       'The bell tolled the same note it had tolled in [' || (ARRAY['The Cold Bell','Ash on the Road','A Verse Unsung','The Harbor Tower','Salt and Iron','What the Choir Knew',
                           'Cinder Gate','The Oath Reversed','Ledger of the Dead','The Long Vigil','Hollow Ridge','A Chain of Hymns',
                           'The Pilgrim''s Toll','Smoke Over the Marsh','Winter Quarters','The Envoy''s Price','Bones Under the Altar','A Second Litany',
                           'The Furnace Door','Night of Drums','The Throne Road','Granite and Glass','The Heir Unnamed','Candle Wars',
                           'The Seventh Seal','Ferry to Nowhere','What the Wolf Heard','The Summit Stair','Dawn Without Bells','The Last Verse'])[(s.k - 1) / 3 - 4]
                       || '](chapter:' || md5('il-chapter:' || ((s.k - 1) / 3 - 4))::uuid || '), and nobody on the road forgot it.'
                   WHEN s.k % 3 = 0 THEN
                       'That night [' || (ARRAY['Maren Voss','Orrin Hale','Sister Ysolde','Cinder Gate','The Iron Choir','Hollow Ridge'])[(s.k / 3 - 1) % 6 + 1]
                       || '](kb:' || md5('il-kb:' || ((s.k / 3 - 1) % 6 + 1))::uuid || ') kept the litany, and nobody who heard it forgot.'
                   ELSE 'The litany went on, verse after verse, through the long dark of scene ' || s.k || '.'
               END,
               s.k)),
       '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/
FROM (
    SELECT k,
           CASE WHEN k < 90 THEN 500 + (k * 811) % 1779
                ELSE 124870 - (SELECT sum(500 + (j * 811) % 1779) FROM generate_series(1, 89) AS j) END AS n
    FROM generate_series(1, 90) AS k
) s;

INSERT INTO prose_revision (scene_id, body, source, created_by)
SELECT p.scene_id, p.body, 'import', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/
FROM prose p JOIN scene s ON s.id = p.scene_id
WHERE s.book_id = '0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/;
