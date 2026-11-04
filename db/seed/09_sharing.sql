-- #80: a second dev user to share with. Ana signs in with the same dev password as JD ("codex-dev-password") and has
-- view on Product Engineering and write on the whole Forsaken Gods series (so on its three books).
INSERT INTO app_user (id, email, display_name, password_hash)
VALUES ('0199c0de-0000-7000-8000-000100000002'::uuid /*Ana*/, 'ana@codex.local', 'Ana Kowalczyk',
        'AQAAAAIAAYagAAAAEIB3Ga2km75rkbWzznF2YVrkWsXTZMgGDODM9zTK9V4R2nsHHOW/tF+v2wD54H4FYQ==');

INSERT INTO book_grant (book_id, user_id, role, granted_by)
VALUES
    ('0199c0de-0000-7000-8000-00030000000a'::uuid /*ProductEngineering*/, '0199c0de-0000-7000-8000-000100000002'::uuid /*Ana*/,
     'view', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/),
    ('0199c0de-0000-7000-8000-000200000001'::uuid /*ForsakenGods*/, '0199c0de-0000-7000-8000-000100000002'::uuid /*Ana*/,
     'write', '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/);
