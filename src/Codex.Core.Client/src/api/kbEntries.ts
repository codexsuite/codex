import { api, type RequestOptions } from './client';
import type { KbCategory, KbFieldDef } from './kb';

// The Knowledge base entry window's client. C#: Codex.Contracts.KnowledgeBase.
export interface KbCustomField {
  label: string;
  value: string;
}

export interface KbSource {
  id: string;
  title: string;
  /** Clearance status: cleared, needs_check, risk or not_cited. */
  status: string;
  reason: string;
}

export interface KbRelated {
  id: string;
  name: string;
  categoryName: string;
}

export interface KbUseKind {
  /** mention, summary, linked, beat, beat_context, timeline, printed or text. */
  kind: string;
  count: number;
}

export interface KbUse {
  sceneId: string;
  bookId: string;
  chapterNumber: number | null;
  chapterTitle: string;
  /** `5.2`, or the scene number in an unnumbered chapter. */
  number: string;
  kinds: KbUseKind[];
  snippet: string;
}

export interface KbTimelineNote {
  id: string;
  sceneId: string;
  bookId: string;
  sceneNumber: string;
  body: string;
}

export interface KbEntry {
  id: string;
  bookId: string;
  categoryId: string;
  categoryName: string;
  categoryColour: string | null;
  fieldDefs: KbFieldDef[];
  name: string;
  slug: string;
  summary: string;
  body: string;
  fields: Record<string, string>;
  customFields: KbCustomField[];
  aliases: string[];
  alwaysInContext: boolean;
  tags: string[];
  excludeFromAutoContext: boolean;
  positionInCategory: number;
  categoryEntryCount: number;
  source: KbSource | null;
  related: KbRelated[];
  usedIn: KbUse[];
  timeline: KbTimelineNote[];
  createdLabel: string;
  updatedLabel: string;
}

export interface CreateKbEntryRequest {
  categoryId: string;
  name: string;
  /** Create it although another entry already has the name or an alias. */
  allowDuplicate?: boolean;
}

export interface PatchKbEntryRequest {
  name?: string;
  summary?: string;
  body?: string;
  fields?: Record<string, string>;
  customFields?: KbCustomField[];
  aliases?: string[];
  alwaysInContext?: boolean;
  categoryId?: string;
  tags?: string[];
  excludeFromAutoContext?: boolean;
  /** A rename keeps the old name as an alias unless this is false. */
  keepOldName?: boolean;
  /** Save a name or alias that another entry already answers to (the server refuses it with a 409 otherwise). */
  allowDuplicate?: boolean;
}

export interface MoveKbEntryRequest {
  afterId: string | null;
}

export interface CreateKbCategoryRequest {
  name: string;
  colour: string;
}

export interface AddKbRelatedRequest {
  entryId: string;
}

export interface MakeKbMentionRequest {
  sceneId: string;
}

export interface KbDeleteImpact {
  mentions: number;
  scenes: number;
  notes: number;
  links: number;
}

/**
 * Everything a delete changed. The server keeps nothing: Undo sends this record back, and a 409 means a scene,
 * summary, beat or entry text changed since.
 */
export interface KbUndoRecord {
  entryId: string;
  name: string;
  scenes: { sceneId: string; version: number; body: string }[];
  summaries: { sceneId: string; summary: string }[];
  beats: { beatId: string; body: string }[];
  entries: { entryId: string; summary: string | null; body: string }[];
  related: string[];
  sceneLinks: string[];
  beatContexts: string[];
}

export const kbEntryKey = (entryId: string) => ['kb-entry', entryId] as const;

export const getKbEntry = (entryId: string, options?: RequestOptions) =>
  api.get<KbEntry>(`/api/kb/entries/${entryId}`, options);

export const createKbEntry = (bookId: string, request: CreateKbEntryRequest) =>
  api.post<KbEntry>(`/api/books/${bookId}/kb/entries`, request);

export const patchKbEntry = (entryId: string, request: PatchKbEntryRequest) =>
  api.patch<KbEntry>(`/api/kb/entries/${entryId}`, request);

export const moveKbEntry = (entryId: string, request: MoveKbEntryRequest) =>
  api.put<void>(`/api/kb/entries/${entryId}/position`, request);

export const createKbCategory = (bookId: string, request: CreateKbCategoryRequest) =>
  api.post<KbCategory>(`/api/books/${bookId}/kb/categories`, request);

export const addKbRelated = (entryId: string, otherId: string) =>
  api.post<KbEntry>(`/api/kb/entries/${entryId}/related`, { entryId: otherId });

export const removeKbRelated = (entryId: string, otherId: string) =>
  api.del<KbEntry>(`/api/kb/entries/${entryId}/related/${otherId}`);

export const makeKbMention = (entryId: string, sceneId: string) =>
  api.post<KbEntry>(`/api/kb/entries/${entryId}/make-mention`, { sceneId });

export const getKbDeleteImpact = (entryId: string) =>
  api.get<KbDeleteImpact>(`/api/kb/entries/${entryId}/delete-impact`);

export const deleteKbEntry = (entryId: string) =>
  api.del<KbUndoRecord>(`/api/kb/entries/${entryId}`);

export const restoreKbEntry = (record: KbUndoRecord) =>
  api.post<KbEntry>(`/api/kb/entries/${record.entryId}/restore`, record);

export interface KbAliasCheck {
  /** The other entry that already answers to the alias, or null. */
  collidesWith: string | null;
  matches: number;
  /** Short (3 letters or fewer) or common (25 or more matches). */
  warn: boolean;
}

export interface KbMergeImpact {
  intoName: string;
  sceneLinks: number;
  additions: number;
  related: number;
  newAliases: string[];
}

export interface PatchKbCategoryRequest {
  name?: string;
  colour?: string;
}

export const checkKbAlias = (entryId: string, alias: string) =>
  api.get<KbAliasCheck>(
    `/api/kb/entries/${entryId}/alias-check?alias=${encodeURIComponent(alias)}`,
  );

export const getKbMergeImpact = (entryId: string, intoId: string) =>
  api.get<KbMergeImpact>(`/api/kb/entries/${entryId}/merge-impact?intoId=${intoId}`);

export const mergeKbEntry = (entryId: string, intoId: string) =>
  api.post<KbEntry>(`/api/kb/entries/${entryId}/merge`, { intoId });

export const patchKbCategory = (categoryId: string, request: PatchKbCategoryRequest) =>
  api.patch<KbCategory>(`/api/kb/categories/${categoryId}`, request);

export const getKbCategoryDeleteImpact = (categoryId: string) =>
  api.get<{ entries: number }>(`/api/kb/categories/${categoryId}/delete-impact`);

export const deleteKbCategory = (categoryId: string) =>
  api.del<void>(`/api/kb/categories/${categoryId}`);

/** `POST /api/kb/entries/{id}/promote`: "Move to series". Without `merge`, a same-named series entry answers 409 needs_confirmation. */
export const promoteKbEntry = (entryId: string, merge = false) =>
  api.post<KbEntry>(`/api/kb/entries/${entryId}/promote`, { merge });
