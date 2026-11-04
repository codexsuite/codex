-- #80: sharing. The owner stays on book.owner_id and never gets a grant row; book_grant lists everyone else who may
-- open a book or a whole series, at one of three roles. book_id is a book or a series row.
CREATE TABLE book_grant (
    book_id     uuid NOT NULL REFERENCES book (id) ON DELETE CASCADE,
    user_id     uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    role        text NOT NULL CHECK (role IN ('view', 'comment', 'write')),
    granted_by  uuid NOT NULL REFERENCES app_user (id),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (book_id, user_id)
);
CREATE INDEX book_grant_user_idx ON book_grant (user_id);

-- Backs up the command layer's 409: the owner is never a grant row, so "owner, or a grant" stays one rule.
CREATE FUNCTION book_grant_not_owner() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM book WHERE id = NEW.book_id AND owner_id = NEW.user_id) THEN
        RAISE EXCEPTION 'book_grant: user % owns book %', NEW.user_id, NEW.book_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER book_grant_not_owner_trg
    BEFORE INSERT OR UPDATE ON book_grant
    FOR EACH ROW EXECUTE FUNCTION book_grant_not_owner();

-- Who may open what, and at which role: the one definition every access check and list query reads.
-- Sources: the owner; a direct grant on the book or series row; a series grant, inherited by each of its books; and
-- a grant on one book, which gives view on its series row so the book can read the series' shared KB, labels and
-- style guide. The highest role per book and user wins (role_rank: view 1, comment 2, write 3, owner 4).
CREATE VIEW book_access AS
WITH sources (book_id, user_id, role) AS (
    SELECT b.id, b.owner_id, 'owner' FROM book b
    UNION ALL
    SELECT g.book_id, g.user_id, g.role FROM book_grant g
    UNION ALL
    SELECT b.id, g.user_id, g.role FROM book b JOIN book_grant g ON g.book_id = b.series_id
    UNION ALL
    SELECT b.series_id, g.user_id, 'view' FROM book b JOIN book_grant g ON g.book_id = b.id
    WHERE b.series_id IS NOT NULL
),
ranked AS (
    SELECT book_id, user_id, role,
           CASE role WHEN 'owner' THEN 4 WHEN 'write' THEN 3 WHEN 'comment' THEN 2 ELSE 1 END AS role_rank
    FROM sources
)
SELECT book_id, user_id,
       (array_agg(role ORDER BY role_rank DESC))[1] AS role,
       max(role_rank)                               AS role_rank
FROM ranked
GROUP BY book_id, user_id;
