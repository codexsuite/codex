-- #13 phase 6 (template management): every layout template version keeps its package, so a book pinned to an older
-- version still renders after the app ships a newer built-in, and a user's fork or upload renders on any host.
-- package_zip is the package's files as one .zip (TemplateArchive.Zip; uploads are capped at 25 MB, built-ins are a few
-- hundred KB), kept with its version row so the two are written and deleted together. package_sha256 is
-- TemplatePackage.ContentHash of those files (the built-in sync compares it with the folder on disk). package_asset_id
-- stays unused: asset rows need an owner, and built-in packages have none.
ALTER TABLE layout_template_version
    ADD COLUMN package_zip    bytea,
    ADD COLUMN package_sha256 text;
