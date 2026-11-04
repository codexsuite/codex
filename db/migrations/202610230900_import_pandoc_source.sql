-- #13: a pandoc book folder (chapter files of Markdown with raw LaTeX, in the order of a pandoc defaults .yaml, and the
-- images they place) is its own import source.
ALTER TABLE book_import DROP CONSTRAINT book_import_source_check;
ALTER TABLE book_import ADD CONSTRAINT book_import_source_check
    CHECK (source IN ('markdown', 'novelcrafter', 'docx', 'codex_zip', 'pandoc'));
