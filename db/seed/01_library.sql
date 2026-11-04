-- Library: the author, three series and fifteen books (issue #16 Part 1, 1.1 and 1.2).
-- Every book row fires book_create_default_act; 02_structure.sql renames those acts.
-- Recency ("2h ago", "30 Sep") is stamped at the end of 05_prose.sql, after the prose triggers have run.
-- Generated from templates: named ids are SeedIds (db/seed/ids.csv).

INSERT INTO app_user (id, email, display_name)
VALUES ('0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'jd@codex.local', 'Joel Dickson');

INSERT INTO book (id, kind, owner_id, title, description, author_name)
VALUES
    ('0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,       'series', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Forsaken Gods',       'tabletop campaign setting', 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,         'series', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'The Ashen Crown',     'epic fantasy quartet',      'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000200000003'::uuid /*TidewaterChronicles*/, 'series', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Tidewater Chronicles', 'coastal mystery',          'Joel Dickson');

INSERT INTO book (id, owner_id, series_id, series_number, title, status, target_word_count, author_name)
VALUES
    ('0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/,        '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,        1, 'Players Guide',          'revising',  60000,  'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000002'::uuid /*GamemastersCodex*/,    '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,        2, 'Gamemaster''s Codex',    'writing',   90000,  'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000003'::uuid /*BestiaryOfTheFallen*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,        3, 'Bestiary of the Fallen', 'planning',  50000,  'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000004'::uuid /*EmberOath*/,           '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,          1, 'Ember Oath',             'published', 110000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000005'::uuid /*IronLitany*/,          '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,          2, 'Iron Litany',            'published', 120000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000006'::uuid /*HollowThrone*/,        '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,          3, 'Hollow Throne',          'revising',  120000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000007'::uuid /*LastCinder*/,          '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000002'::uuid /*AshenCrown*/,          4, 'Last Cinder',            'planning',  120000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000008'::uuid /*Saltmarsh*/,           '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000003'::uuid /*TidewaterChronicles*/, 1, 'Saltmarsh',              'published', 85000,  'Joel Dickson'),
    ('0199c0de-0000-7000-8000-000300000009'::uuid /*Undertow*/,            '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000200000003'::uuid /*TidewaterChronicles*/, 2, 'Undertow',               'writing',   85000,  'Joel Dickson');

INSERT INTO book (id, owner_id, title, status, target_word_count, author_name)
VALUES
    ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/,   '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Product Engineering',      'writing',   80000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-00030000000b'::uuid /*ProductEngineeringV1*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Product Engineering (v1)', 'draft',     80000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-00030000000c'::uuid /*TheQuietAlgorithm*/,    '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'The Quiet Algorithm',      'writing',   70000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-00030000000d'::uuid /*SaltwaterLedger*/,      '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Saltwater Ledger',         'published', 90000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-00030000000e'::uuid /*Northbound*/,           '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Northbound',               'planning',  60000, 'Joel Dickson'),
    ('0199c0de-0000-7000-8000-00030000000f'::uuid /*HymnsForMachines*/,     '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, 'Hymns for Machines',       'draft',     40000, 'Joel Dickson');

-- Labels (issue #16 1.5, labels part). Product Engineering's four, and the Forsaken Gods series label.
INSERT INTO label (id, book_id, name, color, position)
VALUES
    ('0199c0de-0000-7000-8000-000c00000001'::uuid /*ReadyToWrite*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Ready to write', 'ok',     'a0'),
    ('0199c0de-0000-7000-8000-000c00000002'::uuid /*Drafting*/,     '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Drafting',       'info',   'a1'),
    ('0199c0de-0000-7000-8000-000c00000003'::uuid /*NeedsResearch*/,'0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Needs research', 'warn',   'a2'),
    ('0199c0de-0000-7000-8000-000c00000004'::uuid /*MaybeCut*/,     '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'Maybe cut',      'danger', 'a3'),
    ('0199c0de-0000-7000-8000-000c00000005'::uuid /*Playtested*/,   '0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/,      'Playtested',     'ok',     'a0');
