-- Prose, beat lines, the pending AI draft and recency stamps (issue #16 Part 1, 1.2 to 1.4).
-- Word counts come from the prose (markdown_word_count): Product Engineering's scenes are written to the Plan-card
-- counts of #16 1.3; Ch 4 Sc 1 and Ch 8 Sc 1 hold only their quoted words (214 + 17 and 128 + 34); the other books
-- carry short filler. Filler is deterministic: a fixed vocabulary walked by a formula, never random.
-- Canonical prose form (db/prose-corpus/README.md): blocks separated by one blank line, one trailing newline,
-- the empty body is zero bytes. No kb:/ref:/::kb-addition here: the embeds seed adds those.

-- n words of filler in paragraphs of psize words (sentences of 12); the same (n, seed, psize) always gives the same text.
CREATE FUNCTION pg_temp.filler(n integer, seed integer, psize integer DEFAULT 60) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    WITH vocab AS (
        SELECT ARRAY[
            'team','ship','outcome','metric','customer','release','feedback','batch','slice','flag',
            'discovery','measure','learn','cycle','review','backlog','roadmap','delivery','service','failure',
            'incident','deploy','pipeline','quality','speed','safety','trust','scope','budget','estimate',
            'plan','goal','signal','noise','habit','practice','method','craft','system','design',
            'owner','engineer','product','market','value','risk','cost','time','story','sprint',
            'demo','retro','queue','limit','flow','wait','handoff','review','test','build',
            'fix','launch','watch','adapt'] AS w
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

-- Blocks joined with one blank line, empty ones skipped, one trailing newline; no blocks gives the empty body.
CREATE FUNCTION pg_temp.blocks(VARIADIC parts text[]) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    SELECT coalesce(string_agg(p, E'\n\n' ORDER BY ord) || E'\n', '')
    FROM unnest(parts) WITH ORDINALITY AS t (p, ord)
    WHERE p <> ''
$$;

CREATE FUNCTION pg_temp.beat_line(id uuid) RETURNS text
LANGUAGE sql IMMUTABLE AS $$ SELECT '::beat{id="' || id || '"}' $$;

CREATE FUNCTION pg_temp.wc(t text) RETURNS integer
LANGUAGE sql IMMUTABLE AS $$ SELECT markdown_word_count(t) $$;

-- A lead sentence followed by filler, n words in total.
CREATE FUNCTION pg_temp.lead_to(n integer, lead text, seed integer, psize integer DEFAULT 60) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    SELECT CASE WHEN n > markdown_word_count(lead)
                THEN lead || E'\n\n' || pg_temp.filler(n - markdown_word_count(lead), seed, psize)
                ELSE lead END
$$;

-- A single-paragraph block quote of exactly n words that opens with lead.
CREATE FUNCTION pg_temp.quote_of(n integer, lead text, seed integer) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    SELECT '> ' || lead || ' ' || pg_temp.filler(n - markdown_word_count(lead), seed, 100000)
$$;

-- ---------------------------------------------------------------------------
-- Product Engineering
-- ---------------------------------------------------------------------------
INSERT INTO prose (scene_id, body, updated_by)
VALUES
    -- Ch 1 Sc 1, 1,840 words
    ('0199c0de-0000-7000-8000-000600000001'::uuid /*Ch1Sc1*/,
     pg_temp.blocks(pg_temp.lead_to(1840,
         'I wrote this book after ten years of watching good teams ship the wrong thing, on time.', 1)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 2 Sc 1, 1,210 words, with links to Ch 5 and Ch 7
    ('0199c0de-0000-7000-8000-000600000002'::uuid /*Ch2Sc1*/,
     pg_temp.blocks(pg_temp.lead_to(1210,
         'This book is for engineers who want to own what ships. If you are short on time, read [Owning Outcomes](chapter:' || '0199c0de-0000-7000-8000-000500000005'::uuid /*Ch5*/ || ') first, then [Shipping Small](chapter:' || '0199c0de-0000-7000-8000-000500000007'::uuid /*Ch7*/ || ') for the practical half.', 2)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 3 Sc 1, 2,650 words: a hidden beat with its prose written under it
    ('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/,
     pg_temp.blocks(pg_temp.beat_line('0199c0de-0000-7000-8000-000700000001'::uuid /*Ch3Sc1EarlyAgileYears*/),
         pg_temp.lead_to(2650,
             'The beginnings in Agile were a reaction to heavyweight process. Extreme Programming, as Kent Beck described it, gave teams a small set of habits and a lot of feedback.', 3)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 3 Sc 2, 1,980 words, with a 7-word quote from the Agile Manifesto
    ('0199c0de-0000-7000-8000-000600000004'::uuid /*Ch3Sc2*/,
     pg_temp.blocks(pg_temp.lead_to(1980,
         'The Agile Manifesto opens with a single value: "Individuals and interactions over processes and tools". Scrum followed within a decade, and with it a vocabulary of sprints and ceremonies.', 4)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 3 Sc 3: a beat, no prose yet
    ('0199c0de-0000-7000-8000-000600000005'::uuid /*Ch3Sc3*/,
     pg_temp.blocks(pg_temp.beat_line('0199c0de-0000-7000-8000-000700000002'::uuid /*Ch3Sc3BadScrum*/)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 4 Sc 1: a beat and only its quoted words, 214 from The Velocity Trap and 17 from Goodhart
    ('0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/,
     pg_temp.blocks(pg_temp.beat_line('0199c0de-0000-7000-8000-000700000003'::uuid /*Ch4Sc1MeasuringOutput*/),
         pg_temp.quote_of(214, 'Velocity measures how far a team moved, never whether it moved toward anything.', 41),
         '> Any observed statistical regularity will tend to collapse once pressure is placed upon it for control purposes.'),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 4 Sc 2: empty
    ('0199c0de-0000-7000-8000-000600000007'::uuid /*Ch4Sc2*/, '', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 5 Sc 1, 3,120 words: two beats, both with prose
    ('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/,
     pg_temp.blocks(
         'Marty Cagan argues that engineers belong in product discovery, and the Payments Rewrite is the cautionary tale of a team that stayed out of it.',
         pg_temp.filler(1900 - pg_temp.wc('Marty Cagan argues that engineers belong in product discovery, and the Payments Rewrite is the cautionary tale of a team that stayed out of it.'), 5),
         pg_temp.beat_line('0199c0de-0000-7000-8000-000700000004'::uuid /*Ch5Sc1EngineersInDiscovery*/),
         pg_temp.filler(800, 6),
         pg_temp.beat_line('0199c0de-0000-7000-8000-000700000005'::uuid /*Ch5Sc1WhatChanged*/),
         pg_temp.filler(420, 7)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 5 Sc 2: a beat; its draft is pending (ai_generation below)
    ('0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/,
     pg_temp.blocks(pg_temp.beat_line('0199c0de-0000-7000-8000-000700000006'::uuid /*Ch5Sc2OutcomeMetrics*/)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 6 Sc 1, 640 words, with an unmarked quotation
    ('0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/,
     pg_temp.blocks(pg_temp.lead_to(640,
         'When the engineers ran the interviews at the Team Atlas offsite, the facilitator said, "The customer is not in the building", and everyone laughed.', 8)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 6 Sc 2: empty
    ('0199c0de-0000-7000-8000-00060000000b'::uuid /*Ch6Sc2*/, '', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 7 Sc 1, 940 words
    ('0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/,
     pg_temp.blocks(pg_temp.lead_to(940,
         'Thin slices, feature flags and a deploy button anyone can press are what make a small batch size possible.', 9)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 8 Sc 1: a beat and only its quoted words, 128 from The Velocity Trap and a 34-word unmarked passage
    ('0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/,
     pg_temp.blocks(pg_temp.beat_line('0199c0de-0000-7000-8000-000700000007'::uuid /*Ch8Sc1WhatWeDidInstead*/),
         pg_temp.quote_of(128, 'An estimate is a promise made by the person with the least information.', 81),
         'Teams that track velocity learn to protect the number rather than the customer. '
             || pg_temp.filler(34 - pg_temp.wc('Teams that track velocity learn to protect the number rather than the customer.'), 82)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 9 Sc 1, 520 words
    ('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/,
     pg_temp.blocks(pg_temp.lead_to(520,
         'Conway''s Law in practice: design the teams for the architecture you want, because the architecture will copy the teams. As Brooks put it, "Adding manpower to a late software project makes it later".', 10)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),

    -- Ch 10 Sc 1, 410 words, opening with an epigraph from Ship It Friday
    ('0199c0de-0000-7000-8000-00060000000f'::uuid /*Ch10Sc1*/,
     pg_temp.blocks('> Ship it small, ship it Friday, and let the weekend find the bug.',
         pg_temp.lead_to(410 - 13,
             'A reading list and an invitation: pick one practice from this book and try it for a month.', 11)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/);

-- ---------------------------------------------------------------------------
-- Hymns for Machines: Sc 1 points at the archived chapter (a chapter: link keeps working after the chapter is archived).
-- ---------------------------------------------------------------------------
INSERT INTO prose (scene_id, body, updated_by)
VALUES
    ('0199c0de-0000-7000-8000-000600000012'::uuid /*HymnsSc1*/,
     pg_temp.blocks(pg_temp.lead_to(90,
         'The first hymn ends with a pointer to [The Hymn That Was Cut](chapter:' || '0199c0de-0000-7000-8000-00050000000d'::uuid /*HymnsArchivedCh*/ || '), which no longer ships.', 12)),
     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/);

-- ---------------------------------------------------------------------------
-- Filler for every other live scene (not Northbound, which has none): 40 + 10 * book + 15 * scene words, where book
-- is the book's rank among the filler books and scene counts from 1 across the book's live chapters.
-- ---------------------------------------------------------------------------
INSERT INTO prose (scene_id, body, updated_by)
SELECT f.scene_id, pg_temp.blocks(pg_temp.filler(40 + 10 * f.book_rank + 15 * f.scene_rank, f.book_rank * 10 + f.scene_rank)), '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/
FROM (
    SELECT s.id AS scene_id,
           dense_rank() OVER (ORDER BY s.book_id)::integer                     AS book_rank,
           row_number() OVER (PARTITION BY s.book_id ORDER BY c.position, s.position)::integer AS scene_rank
    FROM scene s
    JOIN chapter c ON c.id = s.chapter_id AND c.archived_at IS NULL
    WHERE s.book_id <> '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/
) f
WHERE f.scene_id <> '0199c0de-0000-7000-8000-000600000012'::uuid /*HymnsSc1*/;

-- One 'import' revision per prose row (#16 1.3).
INSERT INTO prose_revision (scene_id, body, source, created_by)
SELECT scene_id, body, 'import', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/ FROM prose;

-- ---------------------------------------------------------------------------
-- The pending AI draft at Ch 5 Sc 2: attempt 1, Claude Opus 5.5, template General purpose (Nonfiction), 3 paragraphs.
-- It is not in the prose yet, so the scene's word count stays 0 until Apply.
-- ---------------------------------------------------------------------------
INSERT INTO ai_generation (id, book_id, scene_id, beat_id, kind, model, request, output, status,
                           input_tokens, output_tokens, created_by, created_at)
VALUES (
    '0199c0de-0000-7000-8000-000900000001'::uuid /*Ch5Sc2Pending*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/, '0199c0de-0000-7000-8000-000700000006'::uuid /*Ch5Sc2OutcomeMetrics*/,
    'beat_prose', 'claude-opus-5-5',
    jsonb_build_object(
        'attempt', 1,
        'template', 'General purpose (Nonfiction)',
        'beat', 'Outcome metrics that don''t rot.',
        'targetWords', 400,
        'context', jsonb_build_array('Goodhart''s Law')),
    'Outcome metrics rot when they become targets. ' || pg_temp.filler(130 - pg_temp.wc('Outcome metrics rot when they become targets.'), 21, 130)
        || E'\n\n' || pg_temp.filler(130, 22, 130)
        || E'\n\n' || pg_temp.filler(130, 23, 130),
    'pending', 1840, 560, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, now() - interval '20 minutes');

-- ---------------------------------------------------------------------------
-- Recency for the Library (#16 1.2). The prose triggers bump last_edited_at, so this runs last. It is written
-- relative to load time (D4); "Today" is now, "Yesterday" is a day back, older books are days back.
-- A series row carries its newest book's value (book_stats takes the greater of the two).
-- ---------------------------------------------------------------------------
UPDATE book b SET last_edited_at = now() - v.ago
FROM (VALUES
    ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/,   interval '0'),
    ('0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,         interval '2 hours'),
    ('0199c0de-0000-7000-8000-000300000002'::uuid /*GamemastersCodex*/,     interval '1 day'),
    ('0199c0de-0000-7000-8000-000300000003'::uuid /*BestiaryOfTheFallen*/,  interval '19 days'),
    ('0199c0de-0000-7000-8000-000300000004'::uuid /*EmberOath*/,            interval '596 days'),
    ('0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/,           interval '281 days'),
    ('0199c0de-0000-7000-8000-000300000006'::uuid /*HollowThrone*/,         interval '5 days'),
    ('0199c0de-0000-7000-8000-000300000007'::uuid /*LastCinder*/,           interval '17 days'),
    ('0199c0de-0000-7000-8000-000300000008'::uuid /*Saltmarsh*/,            interval '137 days'),
    ('0199c0de-0000-7000-8000-000300000009'::uuid /*Undertow*/,             interval '50 days'),
    ('0199c0de-0000-7000-8000-00030000000b'::uuid /*ProductEngineeringV1*/, interval '433 days'),
    ('0199c0de-0000-7000-8000-00030000000c'::uuid /*TheQuietAlgorithm*/,    interval '10 days'),
    ('0199c0de-0000-7000-8000-00030000000d'::uuid /*SaltwaterLedger*/,      interval '151 days'),
    ('0199c0de-0000-7000-8000-00030000000e'::uuid /*Northbound*/,           interval '46 days'),
    ('0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/,     interval '93 days'),
    ('0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,        interval '2 hours'),
    ('0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,          interval '5 days'),
    ('0199c0de-0000-7000-8000-000200000003'::uuid /*TidewaterChronicles*/, interval '50 days')
) AS v (id, ago)
WHERE b.id = v.id;
