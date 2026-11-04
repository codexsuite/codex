-- Layout and print (issue #16 Part 1 and D3): the Players Guide's low-dpi figure and the three built-in layout templates.
-- The asset row has no blob behind it: storage_key names a path that no test reads, and 480 x 320 px at the default
-- column width is well under 300 dpi, which is what Layout's preflight reports. The ::figure line that places the
-- figure, and the raw-LaTeX block in the LaTeX scene, are written by 07_embeds.sql.

INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, width_px, height_px, storage_key, meta)
VALUES ('0199c0de-0000-7000-8000-001100000001'::uuid /*PlayersGuideFigure*/, '0199c0de-0000-7000-8000-000100000001'::uuid /*Jd*/, '0199c0de-0000-7000-8000-000300000001'::uuid /*PlayersGuide*/, 'image', 'fallen-world-map.png', 'image/png',
        48210, 480, 320, 'seed/players-guide/fallen-world-map.png',
        '{"dpi": 72, "source": "sketch scanned at screen resolution"}');

INSERT INTO figure (id, scene_id, asset_id, placement, caption, alt_text)
VALUES ('0199c0de-0000-7000-8000-001200000001'::uuid /*PlayersGuideFigure*/, '0199c0de-0000-7000-8000-000600000010'::uuid /*PlayersGuideFigureScene*/, '0199c0de-0000-7000-8000-001100000001'::uuid /*PlayersGuideFigure*/, 'column',
        'The Fallen World, as the players first see it.', 'A hand-drawn map of the Fallen World with its three kingdoms marked.');

-- Built-in templates (owner_id NULL), each with one published 1.0.0 package.
WITH t AS (
    INSERT INTO layout_template (slug, name, short_name, description)
    VALUES
        ('fantasy-rpg', 'Fantasy RPG (5e style)', 'Fantasy RPG', 'Two columns, parchment sidebars and stat blocks for tabletop books.'),
        ('novel-6x9',   'Novel (6 x 9)',          'Novel',       'A single column book block with classic running heads.'),
        ('manuscript',  'Manuscript',             'Manuscript',  'Double-spaced submission format.')
    RETURNING id, slug
)
INSERT INTO layout_template_version (template_id, major, minor, patch, engine, manifest, changelog, published_at)
SELECT t.id, 1, 0, 0, 'tectonic',
       jsonb_build_object(
           'trims', CASE t.slug WHEN 'novel-6x9' THEN '["6x9", "5.5x8.5"]'::jsonb ELSE '["us-letter", "a4"]'::jsonb END,
           'files', '{"preview.css": {"description": "Preview styles"}}'::jsonb),
       ARRAY['First release'], now()
FROM t;
