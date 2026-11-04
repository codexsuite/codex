-- Structure: acts, chapters, scenes, beats and scene labels (issue #16 Part 1, 1.3 and 1.4).
-- Product Engineering is the full manuscript; Northbound is the empty book (one untitled act, nothing else);
-- every other book gets a few filler chapters and scenes with deterministic ids (md5 of the parent id and an index).
-- Prose, word counts and the pending AI draft are in 05_prose.sql.

-- ---------------------------------------------------------------------------
-- Acts. book_create_default_act gave every book one untitled act 'a0'; give each a stable id.
-- ---------------------------------------------------------------------------
UPDATE act a SET id = n.act_id
FROM (VALUES
    ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/),
    ('0199c0de-0000-7000-8000-00030000000e'::uuid /*Northbound*/,         '0199c0de-0000-7000-8000-000400000004'::uuid /*NorthboundUntitled*/),
    ('0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,       '0199c0de-0000-7000-8000-000400000005'::uuid /*PlayersGuideAct1*/),
    ('0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/,   '0199c0de-0000-7000-8000-000400000006'::uuid /*HymnsAct1*/)
) AS n (book_id, act_id)
WHERE a.book_id = n.book_id;

UPDATE act SET id = md5('act:' || book_id)::uuid
WHERE id NOT IN ('0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/, '0199c0de-0000-7000-8000-000400000004'::uuid /*NorthboundUntitled*/, '0199c0de-0000-7000-8000-000400000005'::uuid /*PlayersGuideAct1*/, '0199c0de-0000-7000-8000-000400000006'::uuid /*HymnsAct1*/);

UPDATE act SET title = 'Introduction' WHERE id = '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/;

INSERT INTO act (id, book_id, position, title)
VALUES
    ('0199c0de-0000-7000-8000-000400000002'::uuid /*TheProductEngineer*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'a1', 'The Product Engineer'),
    ('0199c0de-0000-7000-8000-000400000003'::uuid /*Practices*/,          '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'a2', 'Practices');

-- ---------------------------------------------------------------------------
-- Product Engineering: chapters. Slugs come from the title (set_slug), so preface, about-this-book,
-- owning-outcomes and shipping-small are the four #16 names.
-- ---------------------------------------------------------------------------
INSERT INTO chapter (id, book_id, act_id, position, title)
VALUES
    ('0199c0de-0000-7000-8000-000500000001'::uuid /*Ch1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/,       'a0', 'Preface'),
    ('0199c0de-0000-7000-8000-000500000002'::uuid /*Ch2*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/,       'a1', 'About this book'),
    ('0199c0de-0000-7000-8000-000500000003'::uuid /*Ch3*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/,       'a2', 'A Brief History of Software Practice'),
    ('0199c0de-0000-7000-8000-000500000004'::uuid /*Ch4*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000001'::uuid /*Introduction*/,       'a3', 'What Went Wrong'),
    ('0199c0de-0000-7000-8000-000500000005'::uuid /*Ch5*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000002'::uuid /*TheProductEngineer*/, 'a0', 'Owning Outcomes'),
    ('0199c0de-0000-7000-8000-000500000006'::uuid /*Ch6*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000002'::uuid /*TheProductEngineer*/, 'a1', 'Discovery is Everyone''s Job'),
    ('0199c0de-0000-7000-8000-000500000007'::uuid /*Ch7*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000002'::uuid /*TheProductEngineer*/, 'a2', 'Shipping Small'),
    ('0199c0de-0000-7000-8000-000500000008'::uuid /*Ch8*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000003'::uuid /*Practices*/,          'a0', 'Estimation Without Theatre'),
    ('0199c0de-0000-7000-8000-000500000009'::uuid /*Ch9*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000003'::uuid /*Practices*/,          'a1', 'Teams and Topology'),
    ('0199c0de-0000-7000-8000-00050000000a'::uuid /*Ch10*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000400000003'::uuid /*Practices*/,          'a2', 'Where to From Here');

-- Scenes. status follows the content 05_prose.sql writes: empty, outline (summary only), beats (a beat, no prose), draft.
INSERT INTO scene (id, book_id, chapter_id, position, summary, status)
VALUES
    ('0199c0de-0000-7000-8000-000600000001'::uuid /*Ch1Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000001'::uuid /*Ch1*/,  'a0', 'Why I wrote this: ten years of watching good teams ship the wrong thing, on time.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000002'::uuid /*Ch2Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000002'::uuid /*Ch2*/,  'a0', 'Who it is for, how to read it, and where to go next.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000003'::uuid /*Ch3*/,  'a0', 'The beginnings in Agile: XP, @Kent Beck and the feedback habit.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000004'::uuid /*Ch3Sc2*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000003'::uuid /*Ch3*/,  'a1', 'The @Agile Manifesto and the rise of Scrum.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000005'::uuid /*Ch3Sc3*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000003'::uuid /*Ch3*/,  'a2', 'The rise of "Scrum as a methodology", or "Bad Scrum". @Signs of Bad Scrum', 'beats'),
    ('0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000004'::uuid /*Ch4*/,  'a0', 'Feature factories: measuring output because outcomes are hard. @Goodhart''s Law', 'beats'),
    ('0199c0de-0000-7000-8000-000600000007'::uuid /*Ch4Sc2*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000004'::uuid /*Ch4*/,  'a1', 'Org charts that ship themselves.', 'outline'),
    ('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000005'::uuid /*Ch5*/,  'a0', 'Engineers in discovery, with @The Payments Rewrite as the cautionary tale.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000005'::uuid /*Ch5*/,  'a1', 'Outcome metrics that don''t rot.', 'beats'),
    ('0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000006'::uuid /*Ch6*/,  'a0', 'What happened at @Team Atlas Offsite when the engineers ran the interviews.', 'draft'),
    ('0199c0de-0000-7000-8000-00060000000b'::uuid /*Ch6Sc2*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000006'::uuid /*Ch6*/,  'a1', NULL, 'empty'),
    ('0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000007'::uuid /*Ch7*/,  'a0', 'Thin slices, feature flags, and why batch size is the strategy.', 'draft'),
    ('0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000008'::uuid /*Ch8*/,  'a0', '@Why We Stopped Estimating: what we did instead, and what it cost us.', 'beats'),
    ('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/,  '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000500000009'::uuid /*Ch9*/,  'a0', '@Conway''s Law in practice: design the teams for the architecture you want.', 'draft'),
    ('0199c0de-0000-7000-8000-00060000000f'::uuid /*Ch10Sc1*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-00050000000a'::uuid /*Ch10*/, 'a0', 'Closing: a reading list and an invitation. Epigraph from Ship It Friday.', 'draft');

-- Beats. The ::beat line that places each one in its scene is written by 05_prose.sql
-- (prose_sync_embeds would archive a beat whose line is missing).
INSERT INTO beat (id, scene_id, body, target_word_count, is_hidden, model)
VALUES
    ('0199c0de-0000-7000-8000-000700000001'::uuid /*Ch3Sc1EarlyAgileYears*/,    '0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/, 'The early agile years.',          200, true,  NULL),
    ('0199c0de-0000-7000-8000-000700000002'::uuid /*Ch3Sc3BadScrum*/,           '0199c0de-0000-7000-8000-000600000005'::uuid /*Ch3Sc3*/, 'Bad Scrum.',                      600, false, NULL),
    ('0199c0de-0000-7000-8000-000700000003'::uuid /*Ch4Sc1MeasuringOutput*/,    '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/, 'Measuring output.',               400, false, NULL),
    ('0199c0de-0000-7000-8000-000700000004'::uuid /*Ch5Sc1EngineersInDiscovery*/, '0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, 'Engineers in discovery.',       400, false, NULL),
    ('0199c0de-0000-7000-8000-000700000005'::uuid /*Ch5Sc1WhatChanged*/,        '0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, 'What changed.',                   200, false, NULL),
    ('0199c0de-0000-7000-8000-000700000006'::uuid /*Ch5Sc2OutcomeMetrics*/,     '0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/, 'Outcome metrics that don''t rot.', 400, false, 'claude-opus-5-5'),
    ('0199c0de-0000-7000-8000-000700000007'::uuid /*Ch8Sc1WhatWeDidInstead*/,   '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/, 'What we did instead.',            600, false, NULL);

-- Scene labels.
INSERT INTO scene_label (scene_id, label_id)
VALUES
    ('0199c0de-0000-7000-8000-000600000001'::uuid /*Ch1Sc1*/, '0199c0de-0000-7000-8000-000c00000001'::uuid /*ReadyToWrite*/),
    ('0199c0de-0000-7000-8000-000600000002'::uuid /*Ch2Sc1*/, '0199c0de-0000-7000-8000-000c00000001'::uuid /*ReadyToWrite*/),
    ('0199c0de-0000-7000-8000-000600000005'::uuid /*Ch3Sc3*/, '0199c0de-0000-7000-8000-000c00000003'::uuid /*NeedsResearch*/),
    ('0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/, '0199c0de-0000-7000-8000-000c00000003'::uuid /*NeedsResearch*/),
    ('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, '0199c0de-0000-7000-8000-000c00000002'::uuid /*Drafting*/),
    ('0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/, '0199c0de-0000-7000-8000-000c00000003'::uuid /*NeedsResearch*/),
    ('0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/, '0199c0de-0000-7000-8000-000c00000002'::uuid /*Drafting*/),
    ('0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/, '0199c0de-0000-7000-8000-000c00000002'::uuid /*Drafting*/),
    ('0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/, '0199c0de-0000-7000-8000-000c00000004'::uuid /*MaybeCut*/);

-- ---------------------------------------------------------------------------
-- Players Guide and Hymns for Machines: their named chapter and scenes (the figure and raw-LaTeX scenes are
-- completed by the layout seed; Hymns links to a chapter that was archived).
-- ---------------------------------------------------------------------------
INSERT INTO chapter (id, book_id, act_id, position, title, archived_at)
VALUES
    ('0199c0de-0000-7000-8000-00050000000b'::uuid /*PlayersGuideCh1*/,  '0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,     '0199c0de-0000-7000-8000-000400000005'::uuid /*PlayersGuideAct1*/, 'a0', 'Welcome to the Fallen World', NULL),
    ('0199c0de-0000-7000-8000-00050000000c'::uuid /*HymnsCh1*/,         '0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/, '0199c0de-0000-7000-8000-000400000006'::uuid /*HymnsAct1*/,        'a0', 'The First Hymn',              NULL),
    ('0199c0de-0000-7000-8000-00050000000d'::uuid /*HymnsArchivedCh*/,  '0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/, '0199c0de-0000-7000-8000-000400000006'::uuid /*HymnsAct1*/,        'a1', 'The Hymn That Was Cut',       now());

INSERT INTO scene (id, book_id, chapter_id, position, summary, status)
VALUES
    ('0199c0de-0000-7000-8000-000600000010'::uuid /*PlayersGuideFigureScene*/, '0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,     '0199c0de-0000-7000-8000-00050000000b'::uuid /*PlayersGuideCh1*/, 'a0', 'A map of the Fallen World.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000011'::uuid /*PlayersGuideLatexScene*/,  '0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,     '0199c0de-0000-7000-8000-00050000000b'::uuid /*PlayersGuideCh1*/, 'a1', 'A stat table typeset by hand.', 'draft'),
    ('0199c0de-0000-7000-8000-000600000012'::uuid /*HymnsSc1*/,                '0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/, '0199c0de-0000-7000-8000-00050000000c'::uuid /*HymnsCh1*/,        'a0', 'The opening hymn, with a pointer to the cut one.', 'draft');

-- ---------------------------------------------------------------------------
-- Filler for the other books (not Northbound, which stays empty): three chapters of two scenes, except where a
-- named chapter above already stands in for chapter 1.
-- ---------------------------------------------------------------------------
INSERT INTO chapter (id, book_id, act_id, position, title)
SELECT md5('chapter:' || a.id || ':' || n.n)::uuid, a.book_id, a.id, 'a' || (n.n - 1),
       (ARRAY['Opening Moves', 'Rising Water', 'Final Reckoning'])[n.n]
FROM act a
CROSS JOIN generate_series(1, 3) AS n (n)
WHERE a.book_id NOT IN ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-00030000000e'::uuid /*Northbound*/)
  AND NOT (a.book_id IN ('0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/, '0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/) AND n.n = 1);

INSERT INTO scene (id, book_id, chapter_id, position, summary, status)
SELECT md5('scene:' || c.id || ':' || n.n)::uuid, c.book_id, c.id, 'a' || (n.n - 1),
       'Filler scene ' || n.n || '.', 'draft'
FROM chapter c
CROSS JOIN generate_series(1, 2) AS n (n)
WHERE c.book_id NOT IN ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-00030000000e'::uuid /*Northbound*/)
  AND c.archived_at IS NULL
  AND c.id NOT IN ('0199c0de-0000-7000-8000-00050000000b'::uuid /*PlayersGuideCh1*/, '0199c0de-0000-7000-8000-00050000000c'::uuid /*HymnsCh1*/);
