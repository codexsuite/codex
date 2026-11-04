import { linkSceneEntry, kbKey } from '@codex/core/api/kb';
import { createKbEntry, getKbEntry, kbEntryKey, patchKbEntry } from '@codex/core/api/kbEntries';
import { invalidateOutline } from '@codex/core/api/outline';
import type { QueryClient } from '@tanstack/react-query';
import { appendToSummary } from './segments';

/** Where a finished transcript goes. `sceneId` is the scene the recording was started from, when there was one. */
export type Landing =
  | { kind: 'new-entry'; sceneId: string; name: string; categoryId: string }
  | { kind: 'add-to-entry'; entryId: string; sceneId: string | null };

/**
 * What a landing has already done. A landing is three requests; when the second or third fails, trying again must not
 * create the entry twice or write the summary twice, so the retry reuses this record.
 */
export interface LandingProgress {
  entryId?: string;
  summaryWritten?: boolean;
  linked?: boolean;
}

/**
 * Puts a transcript on the Knowledge base with the existing endpoints: a new entry gets it as its summary, an existing
 * entry gets it appended to the summary (read fresh, so a summary edited elsewhere is not overwritten), and a recording
 * started from a scene links the entry to that scene. Then the KB (and the outline, which lists each scene's entries) are
 * refetched.
 */
export async function landTranscript(
  queryClient: QueryClient,
  bookId: string,
  landing: Landing,
  transcript: string,
  progress: LandingProgress,
): Promise<void> {
  let entryId: string;
  if (landing.kind === 'new-entry') {
    entryId = progress.entryId ??= (
      await createKbEntry(bookId, { categoryId: landing.categoryId, name: landing.name })
    ).id;
    if (!progress.summaryWritten) {
      await patchKbEntry(entryId, { summary: transcript });
      progress.summaryWritten = true;
    }
    if (!progress.linked) {
      await linkSceneEntry(landing.sceneId, entryId);
      progress.linked = true;
    }
  } else {
    entryId = progress.entryId = landing.entryId;
    if (!progress.summaryWritten) {
      const current = await getKbEntry(landing.entryId);
      await patchKbEntry(landing.entryId, {
        summary: appendToSummary(current.summary, transcript),
      });
      progress.summaryWritten = true;
    }
    if (landing.sceneId !== null && !progress.linked) {
      await linkSceneEntry(landing.sceneId, landing.entryId);
      progress.linked = true;
    }
  }
  // Refetching is not waited for: the sheet closes as soon as the server has the text.
  void queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
  void queryClient.invalidateQueries({ queryKey: kbEntryKey(entryId) });
  void invalidateOutline(queryClient, bookId);
}
