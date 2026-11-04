-- #29: JD signs in with the dev password written down in docs/auth.md ("codex-dev-password") and is the admin.
-- Dev and test only: the app never applies the seed, and production sets the first admin's password from
-- CODEX_ADMIN_EMAIL / CODEX_ADMIN_PASSWORD at startup instead.
UPDATE app_user
SET password_hash = 'AQAAAAIAAYagAAAAEIB3Ga2km75rkbWzznF2YVrkWsXTZMgGDODM9zTK9V4R2nsHHOW/tF+v2wD54H4FYQ==',
    is_admin = true
WHERE id = '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/;
