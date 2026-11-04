import { api } from './client';
import type { Library, LibraryBook } from './types';

export const coverPalettes = [
  'cover-1',
  'cover-2',
  'cover-3',
  'cover-4',
  'cover-5',
  'cover-6',
  'cover-7',
  'cover-8',
] as const;
export type CoverPalette = (typeof coverPalettes)[number];

/** The server only returns `cover-1..cover-8` (`BookCover.Palette`); anything else is a contract violation. */
export function parseCoverPalette(value: string): CoverPalette {
  const found = coverPalettes.find((name) => name === value);
  if (found === undefined) {
    throw new Error(`Unknown cover palette "${value}"; the server returns cover-1 to cover-8.`);
  }
  return found;
}

export const sortOptions = ['lastEdited', 'title', 'series', 'progress'] as const;
export type LibrarySort = (typeof sortOptions)[number];
export const groupOptions = ['series', 'status', 'none'] as const;
export type LibraryGroup = (typeof groupOptions)[number];
export const statusOptions = [
  'planning',
  'draft',
  'writing',
  'revising',
  'published',
  'archived',
] as const;
export type LibraryStatus = (typeof statusOptions)[number];

export interface LibraryParams {
  q: string;
  sort: LibrarySort;
  group: LibraryGroup;
  status: LibraryStatus | null;
}

export const defaultLibraryParams: LibraryParams = {
  q: '',
  sort: 'lastEdited',
  group: 'series',
  status: null,
};

export const libraryKey = (params: LibraryParams) => ['library', params] as const;

/** The request path: only non-default values are sent. */
export function libraryPath(params: LibraryParams): string {
  const query = new URLSearchParams();
  if (params.q !== '') query.set('q', params.q);
  if (params.sort !== defaultLibraryParams.sort) query.set('sort', params.sort);
  if (params.group !== defaultLibraryParams.group) query.set('group', params.group);
  if (params.status !== null) query.set('status', params.status);
  const text = query.toString();
  return text === '' ? '/api/library' : `/api/library?${text}`;
}

function checkCovers(books: LibraryBook[]) {
  for (const book of books) parseCoverPalette(book.coverPalette);
}

/** `GET /api/library`. An unknown cover palette rejects the whole response (shown as the load error). */
export async function getLibrary(params: LibraryParams, signal?: AbortSignal): Promise<Library> {
  const library = await api.get<Library>(libraryPath(params), { signal });
  checkCovers(library.standalone);
  for (const series of library.series) checkCovers(series.books);
  return library;
}

// Series settings DTOs (C#: Codex.Contracts.Library).
export interface Series {
  id: string;
  name: string;
  description: string | null;
}

export interface PatchSeriesRequest {
  name?: string;
  /** Empty clears it. */
  description?: string;
}

export interface SetSeriesOrderRequest {
  bookIds: string[];
}

export interface SeriesOrder {
  seriesId: string;
  books: { id: string; seriesNumber: number }[];
}

export const seriesApi = {
  patch: (seriesId: string, request: PatchSeriesRequest) =>
    api.patch<Series>(`/api/series/${seriesId}`, request),
  setOrder: (seriesId: string, request: SetSeriesOrderRequest) =>
    api.put<SeriesOrder>(`/api/series/${seriesId}/order`, request),
  archive: (seriesId: string) => api.post<Series>(`/api/series/${seriesId}/archive`),
  restore: (seriesId: string) => api.post<Series>(`/api/series/${seriesId}/restore`),
};

// Creating a book (C#: Codex.Contracts.Library).
export interface CreateBookRequest {
  title: string;
  subtitle?: string;
  seriesId?: string;
  /** Omit in a series to take the next free number. */
  seriesNumber?: number;
  targetWords?: number;
  /** Defaults to `planning`. */
  status?: string;
}

export interface CreatedBook {
  id: string;
  title: string;
  subtitle: string | null;
  seriesId: string | null;
  seriesNumber: number | null;
  status: string;
  targetWords: number | null;
}

export interface NextSeriesNumber {
  seriesId: string;
  nextNumber: number;
}

export const booksApi = {
  create: (request: CreateBookRequest) => api.post<CreatedBook>('/api/books', request),
};

export const nextNumberKey = (seriesId: string) => ['library', 'next-number', seriesId] as const;

/** `GET /api/series/{id}/next-number`: one above the highest number, archived books counted. */
export const getNextSeriesNumber = (seriesId: string, signal?: AbortSignal) =>
  api.get<NextSeriesNumber>(`/api/series/${seriesId}/next-number`, { signal });

// Series and book lifecycle (C#: Codex.Contracts.Library).
export interface CreateSeriesRequest {
  name: string;
  /** The picked order sets Book 001, 002, ...; may be empty. */
  bookIds: string[];
  description?: string;
  authorName?: string;
}

export interface CreatedSeries {
  id: string;
  name: string;
  bookIds: string[];
}

export interface SeriesDeleteImpact {
  seriesId: string;
  name: string;
  books: number;
  categories: { name: string; entries: number }[];
  labels: string[];
}

export interface BookDeleteImpact {
  bookId: string;
  title: string;
  archived: boolean;
  chapters: number;
  scenes: number;
  words: number;
  sources: number;
}

export const lifecycleApi = {
  createSeries: (request: CreateSeriesRequest) => api.post<CreatedSeries>('/api/series', request),
  seriesImpact: (seriesId: string) =>
    api.get<SeriesDeleteImpact>(`/api/series/${seriesId}/delete-impact`),
  deleteSeries: (seriesId: string) => api.del<void>(`/api/series/${seriesId}`),
  bookImpact: (bookId: string) => api.get<BookDeleteImpact>(`/api/books/${bookId}/delete-impact`),
  deleteBook: (bookId: string) => api.del<void>(`/api/books/${bookId}`),
};
