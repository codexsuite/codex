namespace Codex.Application.Features.KnowledgeBase;

// kb_entry_use restricted to one entry, for the queries that ask about a single entry.
//
// The view regex-scans every prose body, summary and beat in the database, and filtering it by entry
// afterwards cannot stop that: the filter sits on a regexp_matches() result, so the planner re-runs the
// whole scan once per row of whatever it joins next (49 times on the seed, more on a database with no
// statistics). Here every source is cut down to the rows that contain the entry's id first, in a CTE
// that is evaluated once, and the regex then runs only on those rows. The columns and rows are the
// view's, for @entryId.
//
// Parameters: @entryId (uuid), @idText (the same id as lower-case text, for the pre-filter).
// Defines the CTE entry_use; start the statement with it.
internal static class KbEntryUseSql
{
    public const string With =
        """
        WITH uses AS MATERIALIZED (
            SELECT p.scene_id, m[2]::uuid AS kb_entry_id,
                   CASE WHEN m[1] = 'kb:' THEN 'mention' ELSE 'printed' END AS kind
            FROM prose p
            CROSS JOIN LATERAL regexp_matches(p.body,
                '(kb:|::(?:spell|statblock)\{kb=")([0-9a-fA-F-]{36})', 'g') AS m
            WHERE strpos(lower(p.body), @idText) > 0
            UNION ALL
            SELECT s.id, m[1]::uuid, 'summary'
            FROM scene s CROSS JOIN LATERAL regexp_matches(s.summary, 'kb:([0-9a-fA-F-]{36})', 'g') AS m
            WHERE strpos(lower(s.summary), @idText) > 0
            UNION ALL
            SELECT scene_id, kb_entry_id, 'linked' FROM scene_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL
            UNION ALL
            SELECT b.scene_id, m[1]::uuid, 'beat'
            FROM beat b CROSS JOIN LATERAL regexp_matches(b.body, 'kb:([0-9a-fA-F-]{36})', 'g') AS m
            WHERE b.archived_at IS NULL AND strpos(lower(b.body), @idText) > 0
            UNION ALL
            SELECT b.scene_id, x.kb_entry_id, 'beat_context'
            FROM beat_kb x JOIN beat b ON b.id = x.beat_id AND b.archived_at IS NULL
            WHERE x.kb_entry_id = @entryId AND x.archived_at IS NULL
            UNION ALL
            SELECT scene_id, kb_entry_id, 'timeline' FROM kb_progression_outline WHERE kb_entry_id = @entryId
        ),
        entry_use AS (
            SELECT u.kb_entry_id, u.scene_id, ss.book_id, ss.chapter_id, co.chapter_number, ss.scene_number,
                   co.reading_order, u.kind, count(*)::integer AS occurrences
            FROM uses u
            JOIN kb_entry e         ON e.id = u.kb_entry_id AND e.archived_at IS NULL
            JOIN scene_stats ss     ON ss.scene_id = u.scene_id
            JOIN book bk            ON bk.id = ss.book_id AND bk.deleted_at IS NULL
            JOIN chapter_outline co ON co.id = ss.chapter_id
            WHERE u.kb_entry_id = @entryId
            GROUP BY 1, 2, 3, 4, 5, 6, 7, 8
        )
        """;
}
