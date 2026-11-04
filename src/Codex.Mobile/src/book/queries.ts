import { getBook, bookKey } from '@codex/core/api/books';
import { getKb, kbKey } from '@codex/core/api/kb';
import { getBookSettings, settingsKey } from '@codex/core/api/settings';
import { outlineKey } from '@codex/core/api/outline';
import { structureApi } from '@codex/core/api/structure';
import { additionApi, additionsKey, manuscriptKey, writeApi } from '@codex/core/api/write';
import { useQuery } from '@tanstack/react-query';

// The reads that more than one screen of a book shares, over the core's query keys: Plan, Write and the scene sheet all
// read the outline; Write and the Knowledge base read the KB; the book bar and the tabs read the header. Using these
// (not a hand-written `useQuery` with the same key) keeps one cache entry, and the core's cache helpers
// (`updateOutlineScene`, `setSceneProse`, `invalidateOutline`) update what these hooks return.

/** `GET /api/books/{id}`: title, series, cover, scope, counts and the caller's role. */
export function useBookHeader(bookId: string) {
  return useQuery({
    queryKey: bookKey(bookId),
    queryFn: ({ signal }) => getBook(bookId, signal),
  });
}

/** `GET /api/books/{id}/outline`: acts, chapters and scenes. */
export function useOutline(bookId: string) {
  return useQuery({
    queryKey: outlineKey(bookId),
    queryFn: ({ signal }) => structureApi.outline(bookId, { signal }),
  });
}

/** `GET /api/books/{id}/kb`: the categories and entry summaries. */
export function useKb(bookId: string) {
  return useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
}

/** `GET /api/books/{id}/manuscript`: every live scene's prose and version, in reading order. */
export function useManuscript(bookId: string) {
  return useQuery({
    queryKey: manuscriptKey(bookId),
    queryFn: ({ signal }) => writeApi.manuscript(bookId, { signal }),
  });
}

/** `GET /api/books/{id}/settings`. */
export function useBookSettings(bookId: string) {
  return useQuery({
    queryKey: settingsKey(bookId),
    queryFn: ({ signal }) => getBookSettings(bookId, signal),
  });
}

/** `GET /api/books/{id}/kb-additions`: what each scene's KB entries add to its context (Plan counts, Write cards, entries). */
export function useAdditions(bookId: string) {
  return useQuery({
    queryKey: additionsKey(bookId),
    queryFn: ({ signal }) => additionApi.list(bookId, { signal }),
  });
}
