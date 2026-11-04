import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.References.QuoteFlagDto. reason is quotation_marks or source_match; why is the server's wording.
// referenceId/referenceLabel are the best-guess source ("Whitlock, 2021") when the scan matched one.
export interface QuoteFlag {
  id: string;
  sceneId: string;
  chapterNumber: number | null;
  chapterTitle: string;
  sceneNumber: number;
  reason: string;
  why: string;
  excerpt: string;
  referenceId: string | null;
  referenceLabel: string | null;
  matchPercent: number | null;
  locator: string | null;
}

export const quoteFlagsKey = (bookId: string) => ['quote-flags', bookId] as const;

export const getQuoteFlags = (bookId: string, options?: RequestOptions) =>
  api.get<QuoteFlag[]>(`/api/books/${bookId}/quote-flags`, options);

/** Both resolutions answer with the book's remaining open flags. */
export const notAQuote = (flagId: string) =>
  api.post<QuoteFlag[]>(`/api/quote-flags/${flagId}/not-a-quote`);

export const otherSource = (flagId: string, note: string) =>
  api.post<QuoteFlag[]>(`/api/quote-flags/${flagId}/other-source`, { note });
