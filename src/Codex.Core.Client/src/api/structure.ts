import { api, type RequestOptions } from './client';

// Outline, scene patch and label DTOs (C#: Codex.Contracts.Structure). Positions are fractional-index sort keys.
export interface LabelRef {
  id: string;
  name: string;
  color: string | null;
}

export interface EntryRef {
  id: string;
  name: string;
}

export interface OutlineScene {
  id: string;
  /** "5.2", or just the scene number inside an unnumbered chapter. */
  number: string;
  title: string | null;
  /** Markdown. */
  summary: string | null;
  /** empty | outline | beats | draft | revised | final */
  status: string;
  position: string;
  words: number;
  hasPendingDraft: boolean;
  labels: LabelRef[];
  linkedEntries: EntryRef[];
}

/** An act's or chapter's opener art (C#: OutlineOpenerDto). `webUrl` is null until the web rendition exists. */
export interface OutlineOpener {
  assetId: string;
  caption: string | null;
  webUrl: string | null;
}

export interface OutlineChapter {
  id: string;
  /** Continuous across acts; null for an unnumbered (Preface) or archived chapter. */
  number: number | null;
  title: string;
  slug: string;
  position: string;
  archived: boolean;
  archivedSceneCount: number;
  scenes: OutlineScene[];
  opener: OutlineOpener | null;
}

export interface OutlineAct {
  id: string;
  title: string;
  position: string;
  chapters: OutlineChapter[];
  opener: OutlineOpener | null;
}

export interface Outline {
  bookId: string;
  acts: OutlineAct[];
}

export interface Label {
  id: string;
  /** The book or series that owns the label. */
  scopeId: string;
  name: string;
  color: string | null;
  sceneCount: number;
}

/** A field left null is unchanged; an empty title or summary clears it. */
export interface PatchSceneRequest {
  summary: string | null;
  title: string | null;
  status: string | null;
  /** The scene's planning notes; the Plan UI does not send it yet. */
  planNotes?: string | null;
}

export interface SetSceneLabelsRequest {
  labelIds: string[];
}

export interface CreateLabelRequest {
  name: string;
  color: string | null;
}

/** A field left null is unchanged; an empty color clears it. */
export interface PatchLabelRequest {
  name: string | null;
  color: string | null;
}

export const structureApi = {
  outline: (bookId: string, options?: RequestOptions) =>
    api.get<Outline>(`/api/books/${bookId}/outline`, options),
  patchScene: (sceneId: string, request: PatchSceneRequest) =>
    api.patch<OutlineScene>(`/api/scenes/${sceneId}`, request),
  setSceneLabels: (sceneId: string, request: SetSceneLabelsRequest) =>
    api.put<LabelRef[]>(`/api/scenes/${sceneId}/labels`, request),
  labels: (bookId: string, options?: RequestOptions) =>
    api.get<Label[]>(`/api/books/${bookId}/labels`, options),
  createLabel: (bookId: string, request: CreateLabelRequest) =>
    api.post<Label>(`/api/books/${bookId}/labels`, request),
  patchLabel: (labelId: string, request: PatchLabelRequest) =>
    api.patch<Label>(`/api/labels/${labelId}`, request),
  deleteLabel: (labelId: string) => api.del<void>(`/api/labels/${labelId}`),
};
