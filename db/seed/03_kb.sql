-- Knowledge base (issue #16 Part 1, 1.5): Product Engineering's five categories and twelve entries with aliases, the
-- Forsaken Gods series category Spells (three entries) and the Ashen Crown series categories Characters and Places.
-- Their books have no book-level knowledge base. Entry slugs come from kb_entry_set_slug.
-- The scene_kb and beat_kb links are here; the kb: mentions in prose and the ::kb-addition line are 07_embeds.sql.

INSERT INTO kb_category (id, book_id, name, tag, color, position)
VALUES
    ('0199c0de-0000-7000-8000-000a00000001'::uuid /*BlogPosts*/,           '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Blog Posts', 'blog',     'category-blog',     'a0'),
    ('0199c0de-0000-7000-8000-000a00000002'::uuid /*Laws*/,                '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Laws',       'law',      'category-law',      'a1'),
    ('0199c0de-0000-7000-8000-000a00000003'::uuid /*Stories*/,             '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Stories',    'story',    'category-story',    'a2'),
    ('0199c0de-0000-7000-8000-000a00000004'::uuid /*Research*/,            '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Research',   'research', 'category-research', 'a3'),
    ('0199c0de-0000-7000-8000-000a00000005'::uuid /*People*/,              '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'People',     'person',   'category-person',   'a4'),
    ('0199c0de-0000-7000-8000-000a00000006'::uuid /*ForsakenGodsSpells*/,  '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,      'Spells',      'spell',     'category-research', 'a0'),
    ('0199c0de-0000-7000-8000-000a00000007'::uuid /*AshenCrownCharacters*/,'0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,        'Characters',  'character', 'category-person',   'a0'),
    ('0199c0de-0000-7000-8000-000a00000008'::uuid /*AshenCrownPlaces*/,    '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,        'Places',      'place',     'category-story',    'a1');

INSERT INTO kb_entry (id, book_id, category_id, name, position, aliases, summary, body)
VALUES
    ('0199c0de-0000-7000-8000-000b00000001'::uuid /*SignsOfBadScrum*/,      '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000001'::uuid /*BlogPosts*/, 'Signs of Bad Scrum', 'a0', ARRAY['Bad Scrum'],
        'Joel''s post on the ceremonies that survive after the point of Scrum has gone.', 'A blog post listing the signs that a team is doing Scrum without the purpose behind it.'),
    ('0199c0de-0000-7000-8000-000b00000002'::uuid /*FeatureFactory*/,       '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000001'::uuid /*BlogPosts*/, 'Feature Factory', 'a1', ARRAY['feature factories'],
        'A team measured on output, not outcomes.', 'The feature factory is a team that ships to a roadmap and never asks whether the shipped thing changed anything.'),
    ('0199c0de-0000-7000-8000-000b00000003'::uuid /*WhyWeStoppedEstimating*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000001'::uuid /*BlogPosts*/, 'Why We Stopped Estimating', 'a2', '{}',
        'The post behind Chapter 8.', 'What happened to planning accuracy when the team stopped giving estimates.'),
    ('0199c0de-0000-7000-8000-000b00000004'::uuid /*ConwaysLaw*/,           '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000002'::uuid /*Laws*/, 'Conway''s Law', 'a0', ARRAY['Conway'],
        'Systems copy the communication structure of the organisation that builds them.', 'Melvin Conway, 1968: organisations design systems that mirror their own communication structures.'),
    ('0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/,         '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000002'::uuid /*Laws*/, 'Goodhart''s Law', 'a1', ARRAY['Goodhart'],
        'When a measure becomes a target, it ceases to be a good measure.', 'Charles Goodhart, 1975, on monetary policy; widely generalised to any metric used for control.'),
    ('0199c0de-0000-7000-8000-000b00000006'::uuid /*BrooksLaw*/,            '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000002'::uuid /*Laws*/, 'Brooks''s Law', 'a2', '{}',
        'Adding manpower to a late software project makes it later.', 'Fred Brooks, The Mythical Man-Month.'),
    ('0199c0de-0000-7000-8000-000b00000007'::uuid /*ThePaymentsRewrite*/,   '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000003'::uuid /*Stories*/, 'The Payments Rewrite', 'a0', ARRAY['payments rewrite'],
        'The cautionary tale of a team kept out of discovery.', 'An 18-month rewrite that shipped on time and solved a problem nobody had.'),
    ('0199c0de-0000-7000-8000-000b00000008'::uuid /*TeamAtlasOffsite*/,     '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000003'::uuid /*Stories*/, 'Team Atlas Offsite', 'a1', ARRAY['Team Atlas'],
        'The offsite where engineers ran the customer interviews.', 'The story that opens Chapter 6.'),
    ('0199c0de-0000-7000-8000-000b00000009'::uuid /*DoraFourKeys*/,         '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000004'::uuid /*Research*/, 'DORA Four Keys', 'a0', ARRAY['DORA'],
        'Deployment frequency, lead time, change failure rate and time to restore.', 'The four delivery metrics from the DORA research programme.'),
    ('0199c0de-0000-7000-8000-000b0000000a'::uuid /*AgileManifesto*/,       '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000004'::uuid /*Research*/, 'Agile Manifesto (2001)', 'a1', '{}',
        'Four values and twelve principles, signed at Snowbird in 2001.', 'The founding statement of the Agile movement.'),
    ('0199c0de-0000-7000-8000-000b0000000b'::uuid /*KentBeck*/,             '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000005'::uuid /*People*/, 'Kent Beck', 'a0', '{}',
        'Creator of Extreme Programming.', 'Author of Extreme Programming Explained.'),
    ('0199c0de-0000-7000-8000-000b0000000c'::uuid /*MartyCagan*/,           '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000a00000005'::uuid /*People*/, 'Marty Cagan', 'a1', ARRAY['Cagan'],
        'Product thinker behind Inspired and Empowered.', 'Argues that engineers belong in product discovery.'),
    ('0199c0de-0000-7000-8000-000b0000000d'::uuid /*Gravetouch*/,           '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/, '0199c0de-0000-7000-8000-000a00000006'::uuid /*ForsakenGodsSpells*/, 'Gravetouch', 'a0', '{}',
        'A necromantic touch that chills the living.', 'Level 1 necromancy. Range: touch.'),
    ('0199c0de-0000-7000-8000-000b0000000e'::uuid /*HollowPrayer*/,         '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/, '0199c0de-0000-7000-8000-000a00000006'::uuid /*ForsakenGodsSpells*/, 'Hollow Prayer', 'a1', '{}',
        'A prayer to a god who is no longer listening.', 'Level 2 divination. Range: self.'),
    ('0199c0de-0000-7000-8000-000b0000000f'::uuid /*EmberWard*/,            '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/, '0199c0de-0000-7000-8000-000a00000006'::uuid /*ForsakenGodsSpells*/, 'Ember Ward', 'a2', '{}',
        'A shimmering ward of coals.', 'Level 2 abjuration. Range: 30 feet.');

-- "+ Knowledge base" on a scene: one row for every scene that mentions an entry with a kb: link (07_embeds.sql),
-- for the scenes whose summary names one with @, and Ch 5 Sc 2's Goodhart's Law (#16's Linked KB column; J8
-- highlights Ch 4 Sc 1 and Ch 5 Sc 2).
INSERT INTO scene_kb (scene_id, kb_entry_id)
VALUES
    ('0199c0de-0000-7000-8000-000600000003'::uuid /*Ch3Sc1*/, '0199c0de-0000-7000-8000-000b0000000b'::uuid /*KentBeck*/),
    ('0199c0de-0000-7000-8000-000600000005'::uuid /*Ch3Sc3*/, '0199c0de-0000-7000-8000-000b00000001'::uuid /*SignsOfBadScrum*/),
    ('0199c0de-0000-7000-8000-000600000006'::uuid /*Ch4Sc1*/, '0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/),
    ('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, '0199c0de-0000-7000-8000-000b0000000c'::uuid /*MartyCagan*/),
    ('0199c0de-0000-7000-8000-000600000008'::uuid /*Ch5Sc1*/, '0199c0de-0000-7000-8000-000b00000007'::uuid /*ThePaymentsRewrite*/),
    ('0199c0de-0000-7000-8000-000600000009'::uuid /*Ch5Sc2*/, '0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/),
    ('0199c0de-0000-7000-8000-00060000000a'::uuid /*Ch6Sc1*/, '0199c0de-0000-7000-8000-000b00000008'::uuid /*TeamAtlasOffsite*/),
    ('0199c0de-0000-7000-8000-00060000000d'::uuid /*Ch8Sc1*/, '0199c0de-0000-7000-8000-000b00000003'::uuid /*WhyWeStoppedEstimating*/),
    ('0199c0de-0000-7000-8000-00060000000e'::uuid /*Ch9Sc1*/, '0199c0de-0000-7000-8000-000b00000004'::uuid /*ConwaysLaw*/);

-- "+ Context" on a beat.
INSERT INTO beat_kb (beat_id, kb_entry_id)
VALUES
    ('0199c0de-0000-7000-8000-000700000003'::uuid /*Ch4Sc1MeasuringOutput*/, '0199c0de-0000-7000-8000-000b00000002'::uuid /*FeatureFactory*/),
    ('0199c0de-0000-7000-8000-000700000006'::uuid /*Ch5Sc2OutcomeMetrics*/,  '0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/);
