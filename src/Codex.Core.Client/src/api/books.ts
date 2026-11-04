import { api } from './client';
import type { BookHeader } from './types';

export const bookKey = (bookId: string) => ['book', bookId] as const;

/** `GET /api/books/{id}`: the header every book screen shows. A 404 surfaces as ApiRequestError. */
export const getBook = (bookId: string, signal?: AbortSignal) =>
  api.get<BookHeader>(`/api/books/${encodeURIComponent(bookId)}`, { signal });
