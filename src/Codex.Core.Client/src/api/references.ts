import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.References. status is cleared, needs_check or risk.
export interface Marker {
  useId: string;
  number: number;
  status: string;
  sourceId: string;
  sourceTitle: string;
  reason: string;
  kind: string;
  quotedWords: number;
  limit: number;
  sceneId: string;
  chapterNumber: number | null;
  sceneNumber: number;
  generationId: string | null;
}

export interface ClearanceSummary {
  cleared: number;
  total: number;
  risks: number;
  needsCheck: number;
  notCited: number;
  badge: number;
  /** Sources whose sent permission request is past its reminder date. */
  remindersDue: number;
}

export const markersKey = (bookId: string) => ['markers', bookId] as const;

export const clearanceSummaryKey = (bookId: string) => ['clearance-summary', bookId] as const;

export const getMarkers = (bookId: string, options?: RequestOptions) =>
  api.get<Marker[]>(`/api/books/${bookId}/markers`, options);

export const getClearanceSummary = (bookId: string, options?: RequestOptions) =>
  api.get<ClearanceSummary>(`/api/books/${bookId}/references/summary`, options);
