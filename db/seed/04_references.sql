-- References (issue #16 Part 1, 1.6): publication settings, the twelve sources, Team Topologies' permission request and
-- Dora's failing link check. Clearance statuses are never stored; reference_clearance works them out from these rows
-- and from the uses and quote flags that 07_embeds.sql adds. (reference_use rows cannot live here: prose_sync_embeds
-- archives any use whose scene has no ref: link, and the prose is inserted after this file runs.)
-- Times are relative to load (D4): Beck and Cagan were cleared 10 days ago, Conway and Brooks 9 days ago, Accelerate
-- 8 days ago (and its Ch 5 Sc 2 use is newer than that), the Team Topologies request went out 16 days ago.

INSERT INTO publication_settings (book_id, citation_style, rules_name, quote_word_limit)
VALUES ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'chicago', 'Default', 300);

INSERT INTO reference (id, book_id, kind, title, authors, year, publisher, container_title, page_range, url, accessed_on,
                       licence, cite_key, kb_entry_id, signed_off_at, signed_off_by, created_by)
VALUES
    -- 1 Beck: cleared
    ('0199c0de-0000-7000-8000-000d00000001'::uuid /*BeckXp*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'Extreme Programming Explained',
     '[{"family":"Beck","given":"Kent"}]', 1999, 'Addison-Wesley', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'beck1999', '0199c0de-0000-7000-8000-000b0000000b'::uuid /*KentBeck*/, now() - interval '10 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 2 Agile Manifesto: a web page whose licence needs a fair-use decision nobody has made
    ('0199c0de-0000-7000-8000-000d00000002'::uuid /*AgileManifestoWeb*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'web', 'Manifesto for Agile Software Development',
     '[{"literal":"Beck et al."}]', 2001, 'agilemanifesto.org', NULL, NULL, 'https://agilemanifesto.org/', (now() AT TIME ZONE 'UTC')::date - 20,
     'copy_in_full', 'agilemanifesto2001', '0199c0de-0000-7000-8000-000b0000000a'::uuid /*AgileManifesto*/, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 3 Goodhart: complete, not yet signed off
    ('0199c0de-0000-7000-8000-000d00000003'::uuid /*GoodhartMonetary*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'paper', 'Problems of Monetary Management: The U.K. Experience',
     '[{"family":"Goodhart","given":"Charles"}]', 1975, 'Reserve Bank of Australia', 'Papers in Monetary Economics', '1-20', NULL, NULL,
     'all_rights_reserved', 'goodhart1975', '0199c0de-0000-7000-8000-000b00000005'::uuid /*GoodhartsLaw*/, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 4 Velocity Trap: 342 quoted words against the 300 allowance, no permission request
    ('0199c0de-0000-7000-8000-000d00000004'::uuid /*VelocityTrap*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'The Velocity Trap',
     '[{"family":"Whitlock","given":"Dana"}]', 2021, 'Northfield Press', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'whitlock2021', NULL, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 5 Conway: cleared
    ('0199c0de-0000-7000-8000-000d00000005'::uuid /*ConwayCommittees*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'paper', 'How Do Committees Invent?',
     '[{"family":"Conway","given":"Melvin E."}]', 1968, 'Thompson Publications', 'Datamation', '28-31', NULL, NULL,
     'all_rights_reserved', 'conway1968', '0199c0de-0000-7000-8000-000b00000004'::uuid /*ConwaysLaw*/, now() - interval '9 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 6 Cagan and Jones: cleared
    ('0199c0de-0000-7000-8000-000d00000006'::uuid /*CaganEmpowered*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'Empowered',
     '[{"family":"Cagan","given":"Marty"},{"family":"Jones","given":"Chris"}]', 2020, 'Wiley', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'cagan2020', '0199c0de-0000-7000-8000-000b0000000c'::uuid /*MartyCagan*/, now() - interval '10 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 7 DORA: licence not recorded, URL redirects (check below), accessed date missing
    ('0199c0de-0000-7000-8000-000d00000007'::uuid /*DoraReport2024*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'report', '2024 State of DevOps Report',
     '[{"literal":"DORA"}]', 2024, 'Google Cloud', NULL, NULL, 'https://dora.dev/research/2024/', NULL,
     'unknown', 'dora2024', '0199c0de-0000-7000-8000-000b00000009'::uuid /*DoraFourKeys*/, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 8 Accelerate: signed off, then a newer use arrived (07_embeds.sql)
    ('0199c0de-0000-7000-8000-000d00000008'::uuid /*Accelerate*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'Accelerate',
     '[{"family":"Forsgren","given":"Nicole"},{"family":"Humble","given":"Jez"},{"family":"Kim","given":"Gene"}]', 2018, 'IT Revolution', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'forsgren2018', NULL, now() - interval '8 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 9 Team Topologies: a figure needs permission; the request was sent and not answered
    ('0199c0de-0000-7000-8000-000d00000009'::uuid /*TeamTopologies*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'Team Topologies',
     '[{"family":"Skelton","given":"Matthew"},{"family":"Pais","given":"Manuel"}]', 2019, 'IT Revolution', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'skelton2019', NULL, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 10 Brooks: cleared
    ('0199c0de-0000-7000-8000-000d0000000a'::uuid /*MythicalManMonth*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'The Mythical Man-Month',
     '[{"family":"Brooks","given":"Frederick P."}]', 1995, 'Addison-Wesley', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'brooks1995', '0199c0de-0000-7000-8000-000b00000006'::uuid /*BrooksLaw*/, now() - interval '9 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- 11 Ship It Friday: a lyric always needs permission; no request yet
    ('0199c0de-0000-7000-8000-000d0000000b'::uuid /*ShipItFriday*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'song', 'Ship It Friday',
     '[{"literal":"The Retros"}]', 2012, 'Standup Records', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'retros2012', NULL, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    -- Poppendieck: in the library, never cited
    ('0199c0de-0000-7000-8000-000d0000000c'::uuid /*LeanSoftware*/, '0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, 'book', 'Lean Software Development',
     '[{"family":"Poppendieck","given":"Mary"},{"family":"Poppendieck","given":"Tom"}]', 2003, 'Addison-Wesley', NULL, NULL, NULL, NULL,
     'all_rights_reserved', 'poppendieck2003', NULL, NULL, NULL, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/);

-- The DORA address redirects: the check only counts while checked_value still equals the field.
INSERT INTO reference_check (reference_id, field, checked_value, status, http_status, resolved_url, provider, checked_at)
VALUES ('0199c0de-0000-7000-8000-000d00000007'::uuid /*DoraReport2024*/, 'url', 'https://dora.dev/research/2024/', 'redirect', 301,
        'https://dora.dev/research/2024/dora-report/', 'http', now() - interval '2 days');

-- Team Topologies: permission asked 16 days ago, no reply.
INSERT INTO permission_request (id, reference_id, status, to_address, letter, sent_at, remind_at, created_by, created_at)
VALUES ('0199c0de-0000-7000-8000-001000000001'::uuid /*TeamTopologies*/, '0199c0de-0000-7000-8000-000d00000009'::uuid /*TeamTopologies*/, 'sent', 'permissions@itrevolution.example',
        E'Dear IT Revolution,\n\nI am writing to ask permission to reproduce one figure from Team Topologies (2019) in Product Engineering.',
        now() - interval '16 days', now() + interval '3 days', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, now() - interval '16 days');
