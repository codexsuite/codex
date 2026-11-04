import { api, type RequestOptions } from './client';

// Find and replace over a book's text (C#: Codex.Contracts.Manuscript, Find* / Replace*).
export type FindScopeKind = 'book' | 'act' | 'chapter' | 'scene';

export interface FindOptions {
  query: string;
  scopeKind: FindScopeKind;
  scopeId: string | null;
  prose: boolean;
  summaries: boolean;
  beats: boolean;
  matchCase: boolean;
  wholeWord: boolean;
  includeArchived: boolean;
}

export interface FindItem {
  sceneId: string;
  source: 'prose' | 'summary' | 'beat';
  ordinal: number;
  /** replaceable | cited (inside a `ref:` passage or chapter link) | spans_link */
  status: 'replaceable' | 'cited' | 'spans_link';
  before: string;
  text: string;
  after: string;
}

export interface FindResult {
  matches: number;
  replaceable: number;
  cited: number;
  spansLink: number;
  inDrafts: number;
  scenes: number;
  truncated: boolean;
  items: FindItem[];
}

export interface ReplaceUndo {
  prose: { sceneId: string; revisionId: string }[];
  summaries: { sceneId: string; text: string | null }[];
  beats: { beatId: string; text: string }[];
}

export interface ReplaceResult {
  replaced: number;
  scenes: number;
  skippedCited: number;
  skippedSpansLink: number;
  inDrafts: number;
  undo: ReplaceUndo;
}

export const findReplaceApi = {
  find: (bookId: string, options: FindOptions, request?: RequestOptions) =>
    api.post<FindResult>(`/api/books/${bookId}/find`, options, request),
  replace: (
    bookId: string,
    options: FindOptions,
    replacement: string,
    only: { sceneId: string; source: string; ordinal: number } | null,
  ) => api.post<ReplaceResult>(`/api/books/${bookId}/replace`, { ...options, replacement, only }),
  undo: (bookId: string, undo: ReplaceUndo) =>
    api.post<{ restored: number }>(`/api/books/${bookId}/replace/undo`, undo),
};

export const findKey = (bookId: string, options: FindOptions) => ['find', bookId, options] as const;

/** The toast after a replace: "Replaced 1 in 1 scene · Skipped 2 inside cited quotes". */
export function replaceMessage(result: ReplaceResult): string {
  const parts: string[] = [];
  if (result.replaced > 0) {
    parts.push(
      `Replaced ${result.replaced} in ${result.scenes} ${result.scenes === 1 ? 'scene' : 'scenes'}`,
    );
  }
  if (result.skippedCited > 0) parts.push(`Skipped ${result.skippedCited} inside cited quotes`);
  if (result.skippedSpansLink > 0) parts.push(`Skipped ${result.skippedSpansLink} spanning a link`);
  if (result.inDrafts > 0) parts.push(`${result.inDrafts} in a pending draft left alone`);
  return parts.length === 0 ? 'Nothing to replace' : parts.join(' · ');
}
