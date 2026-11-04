-- #29: email-and-password login. password_hash is ASP.NET Core PasswordHasher<T> output (PBKDF2); NULL means the
-- user cannot sign in with a password. security_stamp changes on a password change or an admin reset, which ends
-- every session issued before it. Emails compare case-insensitively.
ALTER TABLE app_user
    ADD COLUMN password_hash         text,
    ADD COLUMN is_admin              boolean NOT NULL DEFAULT false,
    ADD COLUMN must_change_password  boolean NOT NULL DEFAULT false,
    ADD COLUMN disabled_at           timestamptz,
    ADD COLUMN last_login_at         timestamptz,
    ADD COLUMN failed_login_count    integer NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    ADD COLUMN locked_until          timestamptz,
    ADD COLUMN security_stamp        uuid NOT NULL DEFAULT gen_random_uuid();

CREATE UNIQUE INDEX app_user_email_lower_uq ON app_user (lower(email));

-- ASP.NET Core Data Protection keys (the cookie's encryption keys), so a redeploy keeps everyone signed in.
CREATE TABLE data_protection_key (
    id             serial PRIMARY KEY,
    friendly_name  text,
    xml            text NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);
