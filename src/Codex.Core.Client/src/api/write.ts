import type { QueryClient } from '@tanstack/query-core';
import { platform } from '../platform';
import { api, type RequestOptions } from './client';
import { markersKey } from './references';
import { updateOutlineScene } from './outline';

// The Write screen's reads (C#: Codex.Contracts.Manuscript). Later Write slices add their clients here.
export interface ManuscriptScene {
  sceneId: string;
  /** Stored Markdown; empty at version 0 when the scene has no prose yet. */
  body: string;
  version: number;
}

/** Every live scene's prose in reading order. Acts, chapters and numbers come from the outline. */
export interface Manuscript {
  bookId: string;
  scenes: ManuscriptScene[];
}

export const writeApi = {
  manuscript: (bookId: string, options?: RequestOptions) =>
    api.get<Manuscript>(`/api/books/${bookId}/manuscript`, options),
};

export const manuscriptKey = (bookId: string) => ['manuscript', bookId] as const;

/**
 * After a save or a reload: this scene's cached prose is `body` at `version`. Other scenes are untouched. The words
 * may have gained or lost `ref:` links, so the book's source markers are read again.
 */
export function setSceneProse(
  queryClient: QueryClient,
  bookId: string,
  scene: ManuscriptScene,
): void {
  queryClient.setQueryData<Manuscript>(manuscriptKey(bookId), (current) =>
    current
      ? {
          ...current,
          scenes: current.scenes.map((s) => (s.sceneId === scene.sceneId ? scene : s)),
        }
      : current,
  );
  void queryClient.invalidateQueries({ queryKey: markersKey(bookId) });
}

/** After a summary save: the shared outline carries the saved summary (Plan and a remount read it from there). */
export function setSceneSummary(
  queryClient: QueryClient,
  bookId: string,
  sceneId: string,
  summary: string | null,
): void {
  updateOutlineScene(queryClient, bookId, sceneId, (s) => ({ ...s, summary }));
}

/** A body whose save failed (offline, server error) and has not been saved since, with the version it was based on. */
export interface ProseDraft {
  body: string;
  version: number;
}

const draftKey = (bookId: string, sceneId: string) => ['prose-draft', bookId, sceneId] as const;

/** Keeps an unsaved body in the query cache so that leaving the screen does not lose it. */
export function keepProseDraft(
  queryClient: QueryClient,
  bookId: string,
  sceneId: string,
  draft: ProseDraft | null,
): void {
  if (draft) queryClient.setQueryData(draftKey(bookId, sceneId), draft);
  else queryClient.removeQueries({ queryKey: draftKey(bookId, sceneId), exact: true });
}

// The same body is also kept in the platform's persistent storage under the scene id, so a reload or a closed tab does not
// lose an edit that could not be saved (offline, a refused save). Storage can be blocked or full: every call is guarded.
const storedKey = (sceneId: string) => `codex.unsaved.${sceneId}`;

export function storeUnsavedProse(sceneId: string, draft: ProseDraft | null): void {
  try {
    const store = platform().persistentStore();
    if (draft) store.setItem(storedKey(sceneId), JSON.stringify(draft));
    else store.removeItem(storedKey(sceneId));
  } catch {
    // Without storage the edit is still kept in memory (the query cache) while the page is open.
  }
}

function readStoredProse(sceneId: string): ProseDraft | undefined {
  try {
    const raw = platform().persistentStore().getItem(storedKey(sceneId));
    if (raw === null) return undefined;
    const parsed: unknown = JSON.parse(raw);
    if (
      typeof parsed === 'object' &&
      parsed !== null &&
      'body' in parsed &&
      typeof parsed.body === 'string' &&
      'version' in parsed &&
      typeof parsed.version === 'number'
    ) {
      return { body: parsed.body, version: parsed.version };
    }
  } catch {
    // Unreadable: treated as nothing kept.
  }
  return undefined;
}

/** The kept body for a scene, if it was based on the version the server still holds. */
export function readProseDraft(
  queryClient: QueryClient,
  bookId: string,
  sceneId: string,
  serverVersion: number,
): ProseDraft | undefined {
  const draft =
    queryClient.getQueryData<ProseDraft>(draftKey(bookId, sceneId)) ?? readStoredProse(sceneId);
  return draft && draft.version === serverVersion ? draft : undefined;
}

// Beats and block moves (C#: Codex.Contracts.Manuscript). No block index is ever sent: the client sends whole new
// scene bodies with the versions they were edited from, and a stale version is a 409.
export interface Beat {
  id: string;
  sceneId: string;
  /** The instruction the beat carries. */
  body: string;
  targetWordCount: number | null;
  isHidden: boolean;
  contextEntryIds: string[];
  /** The optional bold title on the card; empty when there is none. */
  title: string;
}

export interface BookBeats {
  bookId: string;
  beats: Beat[];
}

export interface PatchBeatRequest {
  body?: string | null;
  isHidden?: boolean | null;
  targetWordCount?: number | null;
  /** An empty string clears the title. */
  title?: string | null;
}

export interface SplitSceneResult {
  newSceneId: string;
  version: number;
  newSceneVersion: number;
}

export interface MoveBlockResult {
  fromVersion: number;
  toVersion: number;
}

export const blockApi = {
  beats: (bookId: string, options?: RequestOptions) =>
    api.get<BookBeats>(`/api/books/${bookId}/beats`, options),
  insertBeat: (sceneId: string, request: { beatId: string; body: string; version: number }) =>
    api.post<{ beatId: string; version: number }>(`/api/scenes/${sceneId}/beats`, request),
  patchBeat: (beatId: string, request: PatchBeatRequest) =>
    api.patch<Beat>(`/api/beats/${beatId}`, request),
  splitScene: (sceneId: string, request: { headBody: string; tailBody: string; version: number }) =>
    api.post<SplitSceneResult>(`/api/scenes/${sceneId}/split`, request),
  moveBlock: (request: {
    fromSceneId: string;
    fromVersion: number;
    fromBody: string;
    toSceneId: string;
    toVersion: number;
    toBody: string;
  }) => api.post<MoveBlockResult>('/api/blocks/move', request),
};

export const beatsKey = (bookId: string) => ['beats', bookId] as const;

/** A beat changed on the server (or optimistically): the cached list carries it. */
export function setBeat(queryClient: QueryClient, bookId: string, beat: Beat): void {
  queryClient.setQueryData<BookBeats>(beatsKey(bookId), (current) =>
    current
      ? { ...current, beats: current.beats.map((b) => (b.id === beat.id ? beat : b)) }
      : current,
  );
}

// Codex additions (C#: Codex.Contracts.Manuscript). Like beats, an insert sends the whole new scene body (holding the
// `::kb-addition` line) with the version it was edited from; the addition text is edited through PATCH.
export interface KbAddition {
  id: string;
  sceneId: string;
  entryId: string;
  body: string;
}

export interface BookKbAdditions {
  bookId: string;
  additions: KbAddition[];
}

export interface InsertKbAdditionRequest {
  additionId: string;
  entryId: string;
  body: string | null;
  sceneBody: string;
  version: number;
}

export interface PatchKbAdditionRequest {
  body?: string | null;
  entryId?: string | null;
}

export const additionApi = {
  list: (bookId: string, options?: RequestOptions) =>
    api.get<BookKbAdditions>(`/api/books/${bookId}/kb-additions`, options),
  insert: (sceneId: string, request: InsertKbAdditionRequest) =>
    api.post<{ progressionId: string; version: number }>(
      `/api/scenes/${sceneId}/kb-additions`,
      request,
    ),
  patch: (additionId: string, request: PatchKbAdditionRequest) =>
    api.patch<KbAddition>(`/api/kb-additions/${additionId}`, request),
};

export const additionsKey = (bookId: string) => ['kb-additions', bookId] as const;

/** An addition changed on the server (or optimistically): the cached list carries it. */
export function setAddition(queryClient: QueryClient, bookId: string, addition: KbAddition): void {
  queryClient.setQueryData<BookKbAdditions>(additionsKey(bookId), (current) =>
    current
      ? {
          ...current,
          additions: current.additions.map((a) => (a.id === addition.id ? addition : a)),
        }
      : current,
  );
}

/** A scene that now exists (after a split): its prose is in the cached manuscript. */
export function addSceneProse(
  queryClient: QueryClient,
  bookId: string,
  scene: ManuscriptScene,
): void {
  queryClient.setQueryData<Manuscript>(manuscriptKey(bookId), (current) =>
    current ? { ...current, scenes: [...current.scenes, scene] } : current,
  );
}

// AI drafts (C#: Codex.Contracts.Ai). A draft belongs to a beat, or to a scene's end when it continues the prose.
export interface Draft {
  id: string;
  sceneId: string;
  beatId: string | null;
  kind: string;
  model: string;
  templateId: string | null;
  template: string | null;
  targetWords: number | null;
  attempt: number;
  status: string;
  text: string;
  words: number;
  retryOfId: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  /** Set on this page when the author pressed Stop: the text is the part received so far. */
  stopped?: boolean;
  /** The AI integration that wrote it; null once that integration is removed. */
  integrationId: string | null;
}

export interface BookDrafts {
  bookId: string;
  drafts: Draft[];
}

export interface GenerateDraftRequest {
  templateId: string | null;
  model: string;
  targetWords: number | null;
  retryOfId: string | null;
  /** One of GET /api/ai/models' integrations; omitted or null: the user's default (#36). */
  integrationId?: string | null;
}

export interface DraftApplyResult {
  version: number;
  body: string;
}

export interface DraftStreamStarted {
  generationId: string;
  attempt: number;
  model: string;
  templateId: string | null;
  template: string | null;
  targetWords: number | null;
}

export interface DraftStreamFinal {
  generationId: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  words: number;
}

/** One line of the generate stream: exactly one member is set. */
export interface DraftStreamEvent {
  started?: DraftStreamStarted;
  text?: string;
  final?: DraftStreamFinal;
  error?: { error: string; code: string };
}

/** Splits the stream's text chunks into its NDJSON lines and parses each. */
export async function* draftEvents(
  chunks: AsyncIterable<string>,
): AsyncGenerator<DraftStreamEvent> {
  let pending = '';
  for await (const chunk of chunks) {
    pending += chunk;
    for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
      const line = pending.slice(0, end).trim();
      pending = pending.slice(end + 1);
      if (line !== '') yield JSON.parse(line) as DraftStreamEvent;
    }
  }
  if (pending.trim() !== '') yield JSON.parse(pending) as DraftStreamEvent;
}

export const draftApi = {
  drafts: (bookId: string, options?: RequestOptions) =>
    api.get<BookDrafts>(`/api/books/${bookId}/drafts`, options),
  /** Generates for a beat; the stream is NDJSON (see `draftEvents`). Aborting `signal` is Stop. */
  generate: (beatId: string, request: GenerateDraftRequest, signal: AbortSignal) =>
    api.stream(`/api/beats/${beatId}/generate`, request, { signal }),
  /** Generates a continuation from the end of the scene. */
  continueScene: (sceneId: string, request: GenerateDraftRequest, signal: AbortSignal) =>
    api.stream(`/api/scenes/${sceneId}/continue`, request, { signal }),
  apply: (generationId: string, version: number) =>
    api.post<DraftApplyResult>(`/api/generations/${generationId}/apply`, { version }),
  undoApply: (generationId: string, version: number) =>
    api.post<DraftApplyResult>(`/api/generations/${generationId}/undo-apply`, { version }),
  discard: (generationId: string) => api.post<Draft>(`/api/generations/${generationId}/discard`),
  undoDiscard: (generationId: string) =>
    api.post<Draft>(`/api/generations/${generationId}/undo-discard`),
};

export const draftsKey = (bookId: string) => ['drafts', bookId] as const;

/** What a draft is attached to: its beat, or `scene:<id>` for a continuation. */
export const draftTarget = (draft: Pick<Draft, 'beatId' | 'sceneId'>): string =>
  draft.beatId ?? sceneTarget(draft.sceneId);

export const sceneTarget = (sceneId: string): string => `scene:${sceneId}`;

/** This draft is now the one waiting at its target (it replaces any other); the list is created if not loaded yet. */
export function setDraft(queryClient: QueryClient, bookId: string, draft: Draft): void {
  queryClient.setQueryData<BookDrafts>(draftsKey(bookId), (current) => ({
    bookId,
    drafts: [
      ...(current?.drafts ?? []).filter((d) => draftTarget(d) !== draftTarget(draft)),
      draft,
    ],
  }));
}

export function removeDraft(queryClient: QueryClient, bookId: string, draftId: string): void {
  queryClient.setQueryData<BookDrafts>(draftsKey(bookId), (current) =>
    current ? { ...current, drafts: current.drafts.filter((d) => d.id !== draftId) } : current,
  );
}
