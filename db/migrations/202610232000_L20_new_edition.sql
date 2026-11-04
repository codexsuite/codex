-- L20.1 (J26): Start a new edition. Like duplicate_book, but the sources come along: every reference, its uses and the
-- attachments that are not a permission letter. Sign-offs are cleared (clearance covers one book's print run) and no
-- permission request is copied, so a source that needed a grant is a risk again until the new edition gets its own.
-- The prose is copied again from the source so the source links (markers) are wrapped, with the new book's ids.
CREATE FUNCTION new_edition(p_source uuid, p_title text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    v_new  uuid;
    v_map  jsonb;
BEGIN
    v_new := duplicate_book(p_source, p_title);  -- leaves its TEMP dup_map (old id -> new id) for this transaction

    CREATE TEMP TABLE IF NOT EXISTS ref_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    TRUNCATE ref_map;
    INSERT INTO ref_map SELECT id, uuidv7() FROM reference WHERE book_id = p_source;

    INSERT INTO reference (id, book_id, kind, title, authors, year, publisher, container_title, volume, issue,
                           page_range, edition, isbn, doi, url, accessed_on, licence, rights_holder,
                           permissions_contact, fair_use_decided_at, fair_use_decided_by, fair_use_note,
                           signed_off_at, signed_off_by, kb_entry_id, cite_key, csl_extra, external_provider,
                           external_key, created_by, created_at, updated_at, archived_at)
    SELECT m.new_id, v_new, r.kind, r.title, r.authors, r.year, r.publisher, r.container_title, r.volume, r.issue,
           r.page_range, r.edition, r.isbn, r.doi, r.url, r.accessed_on, r.licence, r.rights_holder,
           r.permissions_contact, r.fair_use_decided_at, r.fair_use_decided_by, r.fair_use_note,
           NULL, NULL, coalesce(me.new_id, r.kb_entry_id), r.cite_key, r.csl_extra, r.external_provider,
           r.external_key, r.created_by, r.created_at, r.updated_at, r.archived_at
    FROM reference r
    JOIN ref_map m ON m.old_id = r.id
    LEFT JOIN dup_map me ON me.old_id = r.kb_entry_id;

    UPDATE reference n SET merged_into_id = mm.new_id
    FROM reference o, ref_map m, ref_map mm
    WHERE o.book_id = p_source AND m.old_id = o.id AND n.id = m.new_id
      AND o.merged_into_id IS NOT NULL AND mm.old_id = o.merged_into_id;

    -- The prose links name the use (ref:<use id>), so each use gets a new id and the prose is remapped below.
    CREATE TEMP TABLE IF NOT EXISTS use_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    TRUNCATE use_map;
    INSERT INTO use_map SELECT id, uuidv7() FROM reference_use WHERE book_id = p_source AND generation_id IS NULL;

    -- uses in a pending AI draft stay with the draft (not copied)
    INSERT INTO reference_use (id, book_id, reference_id, scene_id, kind, locator, excerpt, asset_id, changed_at,
                               created_by, created_at, updated_at, archived_at)
    SELECT um.new_id, v_new, m.new_id, ms.new_id, u.kind, u.locator, u.excerpt, coalesce(ma.new_id, u.asset_id),
           u.changed_at, u.created_by, u.created_at, u.updated_at, u.archived_at
    FROM reference_use u
    JOIN use_map um ON um.old_id = u.id
    JOIN ref_map m  ON m.old_id = u.reference_id
    JOIN dup_map ms ON ms.old_id = u.scene_id
    LEFT JOIN dup_map ma ON ma.old_id = u.asset_id
    WHERE u.book_id = p_source AND u.generation_id IS NULL;

    INSERT INTO reference_attachment (reference_id, kind, asset_id, archive_url, extracted_text, created_by, created_at)
    SELECT m.new_id, a.kind, coalesce(ma.new_id, a.asset_id), a.archive_url, a.extracted_text, a.created_by, a.created_at
    FROM reference_attachment a
    JOIN ref_map m ON m.old_id = a.reference_id
    LEFT JOIN dup_map ma ON ma.old_id = a.asset_id
    WHERE a.permission_request_id IS NULL AND a.kind <> 'permission_letter';

    SELECT jsonb_object_agg(old_id::text, new_id::text) INTO v_map
    FROM (SELECT old_id, new_id FROM dup_map UNION ALL SELECT old_id, new_id FROM ref_map
          UNION ALL SELECT old_id, new_id FROM use_map) x;

    UPDATE prose np SET body = remap_uuids(op.body, v_map)
    FROM prose op, dup_map ms
    WHERE ms.old_id = op.scene_id AND np.scene_id = ms.new_id;

    RETURN v_new;
END;
$$;
