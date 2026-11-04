-- Embeds (issue #16 Part 1, 1.4 to 1.6): the kb: mentions, the ::kb-addition line, the ref: source links, the
-- Players Guide ::figure line and raw-LaTeX block, the reference uses, the open quote flags. Runs last; it is the only
-- seed file that edits the prose 05_prose.sql wrote.
-- Order matters to the triggers: prose_check_embeds needs every kb_progression and reference_use row to exist before
-- the prose that names it, and prose_sync_embeds archives a use whose link is missing or syncs its excerpt (and a longer
-- excerpt would reopen a signed-off source through reference_use_touch). So the new bodies are built in a temp table,
-- the uses are inserted with the excerpt the prose will carry, and only then is the prose updated.
-- Wrapping a passage in a link or marker never changes a word count (link targets and directive lines do not count),
-- so every figure of 01 to 05 is unchanged.
-- Choices where the data had no prose to hold a use:
--  * Ch 4 Sc 2 (0 words): its Conway quote is a bare marker line, [^](ref:...), which counts 0 words.
--  * Ch 5 Sc 2 (0 words, beat only): its three uses (Goodhart, DORA, Accelerate) are links inside the pending AI draft
--    (generation_id set), which reference_use_outline counts and prose saves leave alone until Apply or Discard.
-- In Ch 4 Sc 1 the Goodhart quote comes before the Velocity Trap quote, so marker numbers follow #16's table.

CREATE FUNCTION pg_temp.lnk(txt text, use_id uuid) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT '[' || txt || '](ref:' || use_id || ')' $$;
CREATE FUNCTION pg_temp.mention(txt text, entry_id uuid) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT '[' || txt || '](kb:' || entry_id || ')' $$;
-- The text prose_sync_embeds keeps in reference_use.excerpt: the linked words, joined by a blank line.
CREATE FUNCTION pg_temp.excerpt(body text, use_id uuid) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT string_agg(m.match[1], E'\n\n' ORDER BY m.ord)
    FROM regexp_matches(body, '\[((?:[^\[\]\\]|\\.)*)\]\(ref:([0-9a-fA-F-]{36})\)', 'g') WITH ORDINALITY AS m (match, ord)
    WHERE m.match[2]::uuid = use_id $$;

CREATE TEMP TABLE new_prose AS
SELECT scene_id, body FROM prose
WHERE scene_id IN ('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/, '0199c0de-0000-7000-8000-000600000004'::uuid /*Ch3Sc2*/, '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/, '0199c0de-0000-7000-8000-000600000007'::uuid /*Ch4Sc2*/, '0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, '0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/,
                   '0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/, '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/, '0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/, '0199c0de-0000-7000-8000-00060000000f'::uuid /*Ch10Sc1*/,
                   '0199c0de-0000-7000-8000-000600000010'::uuid /*PlayersGuideFigureScene*/, '0199c0de-0000-7000-8000-000600000011'::uuid /*PlayersGuideLatexScene*/);

-- Replace one passage, which must occur exactly once in the scene's body.
CREATE FUNCTION pg_temp.edit(p_scene uuid, p_old text, p_new text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE v_body text; v_n integer;
BEGIN
    SELECT body INTO v_body FROM new_prose WHERE scene_id = p_scene;
    v_n := (length(v_body) - length(replace(v_body, p_old, ''))) / length(p_old);
    IF v_n <> 1 THEN
        RAISE EXCEPTION 'embed edit: "%" occurs % times in scene %', p_old, v_n, p_scene;
    END IF;
    UPDATE new_prose SET body = replace(v_body, p_old, p_new) WHERE scene_id = p_scene;
END $$;

-- Ch 3 Sc 1: Kent Beck (mention) and a paraphrase of Beck's book
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/, 'Kent Beck', pg_temp.mention('Kent Beck', '0199c0de-0000-7000-8000-000b0000000b'::uuid /*KentBeck*/));
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/, 'gave teams a small set of habits and a lot of feedback',
    pg_temp.lnk('gave teams a small set of habits and a lot of feedback', '0199c0de-0000-7000-8000-000e00000001'::uuid /*BeckXpCh3Sc1*/));

-- Ch 3 Sc 2: the 7-word Manifesto quote
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000004'::uuid /*Ch3Sc2*/, 'Individuals and interactions over processes and tools',
    pg_temp.lnk('Individuals and interactions over processes and tools', '0199c0de-0000-7000-8000-000e00000002'::uuid /*AgileManifestoWebCh3Sc2*/));

-- Ch 4 Sc 1: beat, Goodhart quote (17 words), Velocity Trap quote (214 words), then the knowledge base addition
UPDATE new_prose n SET body =
    p.parts[1] || E'\n\n'
    || '> ' || pg_temp.lnk(substr(p.parts[3], 3), '0199c0de-0000-7000-8000-000e00000003'::uuid /*GoodhartMonetaryCh4Sc1*/) || E'\n\n'
    || '> ' || pg_temp.lnk(substr(p.parts[2], 3), '0199c0de-0000-7000-8000-000e00000005'::uuid /*VelocityTrapCh4Sc1*/) || E'\n\n'
    || '::kb-addition{id="' || '0199c0de-0000-7000-8000-000800000001'::uuid /*Ch4Sc1GoodhartsLaw*/ || E'"}\n'
FROM (SELECT scene_id, string_to_array(rtrim(body, E'\n'), E'\n\n') AS parts FROM new_prose
      WHERE scene_id = '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/) p
WHERE n.scene_id = p.scene_id AND cardinality(p.parts) = 3;

-- Ch 4 Sc 2: a bare marker for the Conway quote
UPDATE new_prose SET body = pg_temp.lnk('^', '0199c0de-0000-7000-8000-000e00000007'::uuid /*ConwayCommitteesCh4Sc2*/) || E'\n'
WHERE scene_id = '0199c0de-0000-7000-8000-000600000007'::uuid /*Ch4Sc2*/ AND body = '';

-- Ch 5 Sc 1: Cagan and the Payments Rewrite (mentions), a paraphrase of Empowered
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, 'Marty Cagan', pg_temp.mention('Marty Cagan', '0199c0de-0000-7000-8000-000b0000000c'::uuid /*MartyCagan*/));
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, 'Payments Rewrite', pg_temp.mention('Payments Rewrite', '0199c0de-0000-7000-8000-000b00000007'::uuid /*ThePaymentsRewrite*/));
SELECT pg_temp.edit('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, 'argues that engineers belong in product discovery',
    pg_temp.lnk('argues that engineers belong in product discovery', '0199c0de-0000-7000-8000-000e00000009'::uuid /*CaganEmpoweredCh5Sc1*/));

-- Ch 6 Sc 1: Team Atlas (mention); the quotation marks stay unmarked, for the open flag f1
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/, 'Team Atlas', pg_temp.mention('Team Atlas', '0199c0de-0000-7000-8000-000b00000008'::uuid /*TeamAtlasOffsite*/));

-- Ch 7 Sc 1: a paraphrase of Accelerate
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/, 'Thin slices, feature flags and a deploy button anyone can press are what make a small batch size possible',
    pg_temp.lnk('Thin slices, feature flags and a deploy button anyone can press are what make a small batch size possible', '0199c0de-0000-7000-8000-000e0000000c'::uuid /*AccelerateCh7Sc1*/));

-- Ch 8 Sc 1: the 128-word Velocity Trap quote; the 34-word passage after it stays unmarked, for the open flag f2
UPDATE new_prose n SET body =
    p.parts[1] || E'\n\n'
    || '> ' || pg_temp.lnk(substr(p.parts[2], 3), '0199c0de-0000-7000-8000-000e00000006'::uuid /*VelocityTrapCh8Sc1*/) || E'\n\n'
    || p.parts[3] || E'\n'
FROM (SELECT scene_id, string_to_array(rtrim(body, E'\n'), E'\n\n') AS parts FROM new_prose
      WHERE scene_id = '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/) p
WHERE n.scene_id = p.scene_id AND cardinality(p.parts) = 3;

-- Ch 9 Sc 1: Conway's Law (mention), a paraphrase of Conway, the Team Topologies figure marker, Brooks's quote
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/, 'Conway''s Law in practice', pg_temp.mention('Conway''s Law', '0199c0de-0000-7000-8000-000b00000004'::uuid /*ConwaysLaw*/) || ' in practice');
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/, 'design the teams for the architecture you want, because the architecture will copy the teams.',
    pg_temp.lnk('design the teams for the architecture you want, because the architecture will copy the teams', '0199c0de-0000-7000-8000-000e00000008'::uuid /*ConwayCommitteesCh9Sc1*/)
        || '.' || pg_temp.lnk('^', '0199c0de-0000-7000-8000-000e0000000d'::uuid /*TeamTopologiesCh9Sc1*/));
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/, 'Adding manpower to a late software project makes it later',
    pg_temp.lnk('Adding manpower to a late software project makes it later', '0199c0de-0000-7000-8000-000e0000000e'::uuid /*MythicalManMonthCh9Sc1*/));

-- Ch 10 Sc 1: the epigraph is a lyric
SELECT pg_temp.edit('0199c0de-0000-7000-8000-00060000000f'::uuid /*Ch10Sc1*/, '> Ship it small, ship it Friday, and let the weekend find the bug.',
    '> ' || pg_temp.lnk('Ship it small, ship it Friday, and let the weekend find the bug.', '0199c0de-0000-7000-8000-000e0000000f'::uuid /*ShipItFridayCh10Sc1*/));

-- Players Guide: the figure line, and a raw-LaTeX block (a hand-set table) that the word count ignores
UPDATE new_prose SET body = rtrim(body, E'\n') || E'\n\n::figure{id="' || '0199c0de-0000-7000-8000-001200000001'::uuid /*PlayersGuideFigure*/ || E'"}\n'
WHERE scene_id = '0199c0de-0000-7000-8000-000600000010'::uuid /*PlayersGuideFigureScene*/;
UPDATE new_prose SET body = rtrim(body, E'\n') || E'\n\n```{=latex}\n\\begin{tabular}{ll}\nRoll & Result \\\\\n1--3 & Nothing \\\\\n4--6 & The door opens \\\\\n\\end{tabular}\n```\n'
WHERE scene_id = '0199c0de-0000-7000-8000-000600000011'::uuid /*PlayersGuideLatexScene*/;

-- The pending draft at Ch 5 Sc 2: its uses live in the draft's links until Apply or Discard.
CREATE TEMP TABLE new_output AS
SELECT g.id,
       replace(g.output, 'Outcome metrics rot when they become targets.',
               pg_temp.lnk('Outcome metrics rot when they become targets', '0199c0de-0000-7000-8000-000e00000004'::uuid /*GoodhartMonetaryCh5Sc2*/) || '.') AS output
FROM ai_generation g WHERE g.id = '0199c0de-0000-7000-8000-000900000001'::uuid /*Ch5Sc2Pending*/;
UPDATE new_output SET output = (
    SELECT p.parts[1] || E'\n\n'
        || p.parts[2] || pg_temp.lnk('^', '0199c0de-0000-7000-8000-000e0000000a'::uuid /*DoraReport2024Ch5Sc2*/) || E'\n\n'
        || p.parts[3] || pg_temp.lnk('^', '0199c0de-0000-7000-8000-000e0000000b'::uuid /*AccelerateCh5Sc2*/)
    FROM (SELECT string_to_array(output, E'\n\n') AS parts) p
    WHERE cardinality(p.parts) = 3);

-- ---------------------------------------------------------------------------
-- Rows the prose now names. The excerpt is the linked words, exactly as prose_sync_embeds will keep it.
-- ---------------------------------------------------------------------------
INSERT INTO kb_progression (id, kb_entry_id, scene_id, body)
VALUES ('0199c0de-0000-7000-8000-000800000001'::uuid /*Ch4Sc1GoodhartsLaw*/, '0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/, '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/,
        'The reader meets Goodhart''s Law for the first time here, so the next paragraph explains it plainly.');

INSERT INTO reference_use (id, book_id, reference_id, scene_id, kind, locator, excerpt, generation_id, changed_at, created_by)
SELECT u.id, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, u.reference_id, u.scene_id, u.kind, u.locator,
       coalesce(pg_temp.excerpt(coalesce(np.body, nout.output), u.id), ''), u.generation_id, now() - u.age, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/
FROM (VALUES
    ('0199c0de-0000-7000-8000-000e00000001'::uuid /*BeckXpCh3Sc1*/,             '0199c0de-0000-7000-8000-000d00000001'::uuid /*BeckXp*/,            '0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/,  'paraphrase', NULL,         NULL::uuid, interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000002'::uuid /*AgileManifestoWebCh3Sc2*/,  '0199c0de-0000-7000-8000-000d00000002'::uuid /*AgileManifestoWeb*/, '0199c0de-0000-7000-8000-000600000004'::uuid /*Ch3Sc2*/,  'quote',      NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000003'::uuid /*GoodhartMonetaryCh4Sc1*/,   '0199c0de-0000-7000-8000-000d00000003'::uuid /*GoodhartMonetary*/,  '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/,  'quote',      NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000004'::uuid /*GoodhartMonetaryCh5Sc2*/,   '0199c0de-0000-7000-8000-000d00000003'::uuid /*GoodhartMonetary*/,  '0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/,  'paraphrase', NULL,         '0199c0de-0000-7000-8000-000900000001'::uuid /*Ch5Sc2Pending*/, interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000005'::uuid /*VelocityTrapCh4Sc1*/,       '0199c0de-0000-7000-8000-000d00000004'::uuid /*VelocityTrap*/,      '0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/,  'quote',      'pp. 41-43',  NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000006'::uuid /*VelocityTrapCh8Sc1*/,       '0199c0de-0000-7000-8000-000d00000004'::uuid /*VelocityTrap*/,      '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/,  'quote',      'p. 118',     NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000007'::uuid /*ConwayCommitteesCh4Sc2*/,   '0199c0de-0000-7000-8000-000d00000005'::uuid /*ConwayCommittees*/,  '0199c0de-0000-7000-8000-000600000007'::uuid /*Ch4Sc2*/,  'quote',      NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000008'::uuid /*ConwayCommitteesCh9Sc1*/,   '0199c0de-0000-7000-8000-000d00000005'::uuid /*ConwayCommittees*/,  '0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/,  'paraphrase', NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e00000009'::uuid /*CaganEmpoweredCh5Sc1*/,     '0199c0de-0000-7000-8000-000d00000006'::uuid /*CaganEmpowered*/,    '0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/,  'paraphrase', NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e0000000a'::uuid /*DoraReport2024Ch5Sc2*/,     '0199c0de-0000-7000-8000-000d00000007'::uuid /*DoraReport2024*/,    '0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/,  'data',       'Table 5.1',  '0199c0de-0000-7000-8000-000900000001'::uuid /*Ch5Sc2Pending*/, interval '12 days'),
    ('0199c0de-0000-7000-8000-000e0000000b'::uuid /*AccelerateCh5Sc2*/,         '0199c0de-0000-7000-8000-000d00000008'::uuid /*Accelerate*/,        '0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/,  'paraphrase', NULL,         '0199c0de-0000-7000-8000-000900000001'::uuid /*Ch5Sc2Pending*/, interval '3 days'),
    ('0199c0de-0000-7000-8000-000e0000000c'::uuid /*AccelerateCh7Sc1*/,         '0199c0de-0000-7000-8000-000d00000008'::uuid /*Accelerate*/,        '0199c0de-0000-7000-8000-00060000000c'::uuid /*Ch7Sc1*/,  'paraphrase', NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e0000000d'::uuid /*TeamTopologiesCh9Sc1*/,     '0199c0de-0000-7000-8000-000d00000009'::uuid /*TeamTopologies*/,    '0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/,  'figure',     NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e0000000e'::uuid /*MythicalManMonthCh9Sc1*/,   '0199c0de-0000-7000-8000-000d0000000a'::uuid /*MythicalManMonth*/,  '0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/,  'quote',      NULL,         NULL,       interval '12 days'),
    ('0199c0de-0000-7000-8000-000e0000000f'::uuid /*ShipItFridayCh10Sc1*/,      '0199c0de-0000-7000-8000-000d0000000b'::uuid /*ShipItFriday*/,      '0199c0de-0000-7000-8000-00060000000f'::uuid /*Ch10Sc1*/, 'lyric',      NULL,         NULL,       interval '12 days')
) AS u (id, reference_id, scene_id, kind, locator, generation_id, age)
LEFT JOIN new_prose np ON np.scene_id = u.scene_id
LEFT JOIN new_output nout ON nout.id = u.generation_id;

-- Open quote flags: f1 (quotation marks, no marker) and f2 (a 93% match with The Velocity Trap, p. 112).
INSERT INTO quote_flag (id, book_id, scene_id, excerpt, reason, reference_id, match_score, locator)
VALUES ('0199c0de-0000-7000-8000-000f00000001'::uuid /*Ch6Sc1UnmarkedQuote*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/,
        'The customer is not in the building', 'quotation_marks', NULL, NULL, NULL);
INSERT INTO quote_flag (id, book_id, scene_id, excerpt, reason, reference_id, match_score, locator)
SELECT '0199c0de-0000-7000-8000-000f00000002'::uuid /*Ch8Sc1VelocityTrapMatch*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/,
       (string_to_array(rtrim(body, E'\n'), E'\n\n'))[3], 'source_match', '0199c0de-0000-7000-8000-000d00000004'::uuid /*VelocityTrap*/, 0.93, 'p. 112'
FROM new_prose WHERE scene_id = '0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/;

-- ---------------------------------------------------------------------------
-- Write the prose, keep the 'import' revision in step, and apply the draft's links.
-- ---------------------------------------------------------------------------
UPDATE prose p SET body = n.body FROM new_prose n WHERE p.scene_id = n.scene_id AND p.body <> n.body;
UPDATE prose_revision r SET body = n.body FROM new_prose n WHERE r.scene_id = n.scene_id AND r.source = 'import';
UPDATE ai_generation g SET output = o.output FROM new_output o WHERE g.id = o.id;

DROP TABLE new_prose;
DROP TABLE new_output;

-- The prose and figure writes bumped last_edited_at (touch_book_last_edited): put back the offsets 05_prose.sql set
-- for the books touched here. This must stay the last statement.
UPDATE book b SET last_edited_at = now() - v.ago
FROM (VALUES
    ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, interval '0'),
    ('0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,       interval '2 hours')
) AS v (id, ago)
WHERE b.id = v.id;
