import { api } from './client';
import type { SourceDetailsRequest } from './referenceSources';

// C#: Codex.Contracts.References / Codex.Contracts.Sources. Look up, add and import sources.

export type LookupKind = 'isbn' | 'doi' | 'url';

export interface SourceMetadata {
  title: string;
  authors: string[];
  year: number | null;
  publisher: string | null;
  url: string | null;
}

/** existing: already in the book; found: `source` is set; redirect: `redirectTo`; not_found. `value` is the canonical identifier. */
export interface LookupResult {
  outcome: 'existing' | 'found' | 'redirect' | 'not_found';
  value: string;
  source: SourceMetadata | null;
  existingId: string | null;
  existingTitle: string | null;
  existingByline: string | null;
  redirectTo: string | null;
}

/** `id` for a new source, `existingId` when the ISBN, DOI, URL or cite key was already in the book. */
export interface AddSourceResult {
  id: string | null;
  existingId: string | null;
}

export interface ImportItem {
  provider: 'bibtex' | 'ris' | 'csl';
  key: string | null;
  details: SourceDetailsRequest;
}

export interface ImportResult {
  created: number;
  skipped: number;
  skippedTitles: string[];
  duplicates: number;
  untitled: number;
  skippedReasons: string[] | null;
}

export const lookupSource = (bookId: string, kind: LookupKind, value: string) =>
  api.post<LookupResult>(`/api/books/${bookId}/references/lookup`, { kind, value });

export const addSource = (bookId: string, details: SourceDetailsRequest, via: LookupKind | null) =>
  api.post<AddSourceResult>(`/api/books/${bookId}/references`, { details, via });

export const importSources = (bookId: string, items: ImportItem[]) =>
  api.post<ImportResult>(`/api/books/${bookId}/references/import`, { items });

/** What an import would do, written nowhere. */
export const previewImport = (bookId: string, items: ImportItem[]) =>
  api.post<ImportResult>(`/api/books/${bookId}/references/import/preview`, { items });
