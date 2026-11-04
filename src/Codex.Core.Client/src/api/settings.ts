import { api } from './client';
import type { Job } from './jobs';

// Book settings DTOs (C#: Codex.Contracts.Settings).
export interface SeriesOption {
  id: string;
  name: string;
  bookCount: number;
}

export interface SeriesOrderEntry {
  id: string;
  title: string;
  number: number;
  /** The stored value: planning, draft, writing, revising or published. */
  status: string;
}

export interface BookSettings {
  id: string;
  title: string;
  authorName: string | null;
  status: string;
  targetWords: number | null;
  wordCount: number;
  /** Words / target as a whole percent; null without a target. */
  progressPercent: number | null;
  seriesId: string | null;
  seriesName: string | null;
  seriesNumber: number | null;
  seriesOptions: SeriesOption[];
  seriesOrder: SeriesOrderEntry[];
  styleGuide: string | null;
  seriesGuide: string | null;
  /** Archived books are hidden from Library until restored. */
  archived: boolean;
  subtitle: string | null;
  description: string | null;
}

/** A missing field is left alone; empty `authorName`/`styleGuide` clears it; `targetWords` 0 clears it. */
export interface PatchBookSettingsRequest {
  title?: string;
  authorName?: string;
  status?: string;
  targetWords?: number;
  styleGuide?: string;
  subtitle?: string;
  description?: string;
}

export interface SetSeriesNumberRequest {
  number: number;
}

export interface SeriesNumberEntry {
  id: string;
  number: number;
  previousNumber: number;
}

export interface SeriesNumberChange {
  bookId: string;
  seriesId: string;
  number: number;
  previousNumber: number;
  books: SeriesNumberEntry[];
}

/** `seriesId` null leaves the series. */
export interface SeriesMoveRequest {
  seriesId: string | null;
}

export interface SeriesMoveEnd {
  id: string | null;
  name: string | null;
  number: number | null;
}

export interface SeriesMoveStep {
  key: string;
  text: string;
}

export interface SeriesMovePreview {
  bookId: string;
  title: string;
  from: SeriesMoveEnd;
  to: SeriesMoveEnd;
  steps: SeriesMoveStep[];
  notes: string[];
  warnings: string[];
  /** People the target series is shared with, who will see the book once it joins (0 for standalone). */
  targetSeriesSharedWith: number;
  /** People who see the book only through its current series and will lose it. */
  losingAccess: number;
}

/** One citation/permission value; `inherited` while a series book has none of its own. */
export interface PublicationValue<T> {
  value: T;
  inherited: boolean;
}

export type CitationStyle = 'chicago' | 'apa' | 'harvard';
export type PublicationFormat = 'print' | 'ebook' | 'pdf' | 'audio';
export type PublicationField =
  'style' | 'quoteWordLimit' | 'publisher' | 'plannedYear' | 'formats' | 'printRun' | 'territory';

export interface PublicationSettings {
  bookId: string;
  /** Null for a standalone book (no Series chips). */
  seriesName: string | null;
  style: PublicationValue<CitationStyle>;
  rulesName: string;
  quoteWordLimit: PublicationValue<number>;
  /** "" = you publish it yourself; null = not set. */
  publisher: PublicationValue<string | null>;
  plannedYear: PublicationValue<number | null>;
  formats: PublicationValue<PublicationFormat[]>;
  printRun: PublicationValue<number | null>;
  territory: PublicationValue<string | null>;
}

/** A missing field is left alone; a value equal to the series' goes back to inheriting. */
export interface PatchPublicationSettingsRequest {
  style?: CitationStyle;
  quoteWordLimit?: number;
  publisher?: string;
  plannedYear?: number;
  formats?: PublicationFormat[];
  printRun?: number;
  territory?: string;
}

export interface RuleSource {
  referenceId: string;
  title: string;
  number: number | null;
  quotedWords: number;
  /** quote_over_limit, lyric or figure when the source needs permission. */
  reason: string | null;
  atRisk: boolean;
  why: string | null;
}

export interface RulesImpact {
  limit: number;
  riskCount: number;
  sources: RuleSource[];
}

export interface LayoutTemplate {
  name: string;
  description: string | null;
  isUserTemplate: boolean;
  pinnedVersion: string;
  /** Set only when a newer version than the pinned one exists. */
  latestVersion: string | null;
  trimSize: string | null;
  trims: string[];
}

export interface LayoutSettings {
  template: LayoutTemplate | null;
  /** Copyright risks in References; they block print export. */
  riskCount: number;
}

/** The part of `POST /api/books/{id}/assets`' AssetDto the cover needs. */
export interface UploadedImage {
  id: string;
}

export interface DuplicatedBook {
  id: string;
  title: string;
}

/** One snapshot taken when the book was marked Published: a source export (C#: SnapshotDto). */
export interface Snapshot {
  id: string;
  label: string;
  whenLabel: string;
  words: number;
  /** The export job's status: queued, running, succeeded, failed or cancelled. */
  state: string;
  /** Queued behind a running source export. */
  waiting: boolean;
  fileUrl: string | null;
}

/** `GET/POST …/publish` (C#: PublishStateDto). */
export interface PublishState {
  status: string;
  wordCount: number;
  chapterCount: number;
  atRiskCount: number;
  exportRunning: boolean;
  snapshots: Snapshot[];
}

export const publishKey = (bookId: string) => ['publish-state', bookId] as const;

export const settingsKey = (bookId: string) => ['settings', bookId] as const;

const base = (bookId: string) => `/api/books/${encodeURIComponent(bookId)}`;

export const publicationKey = (bookId: string) => ['publication-settings', bookId] as const;
export const rulesImpactKey = (bookId: string, limit: number) =>
  ['rules-impact', bookId, limit] as const;
export const layoutSettingsKey = (bookId: string) => ['layout-settings', bookId] as const;

export const settingsApi = {
  patch: (bookId: string, request: PatchBookSettingsRequest) =>
    api.patch<BookSettings>(`${base(bookId)}/settings`, request),
  setNumber: (bookId: string, request: SetSeriesNumberRequest) =>
    api.put<SeriesNumberChange>(`${base(bookId)}/series-number`, request),
  previewMove: (bookId: string, request: SeriesMoveRequest) =>
    api.post<SeriesMovePreview>(`${base(bookId)}/series-move/preview`, request),
  move: (bookId: string, request: SeriesMoveRequest) =>
    api.post<BookSettings>(`${base(bookId)}/series-move`, request),
  patchPublication: (bookId: string, request: PatchPublicationSettingsRequest) =>
    api.patch<PublicationSettings>(`${base(bookId)}/publication-settings`, request),
  resetPublication: (bookId: string, field: PublicationField) =>
    api.del<PublicationSettings>(`${base(bookId)}/publication-settings/${field}`),
  /** Stores a PNG, JPEG or WebP image (25 MB at most) among the book's assets. */
  uploadImage: (bookId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api.upload<UploadedImage>(`${base(bookId)}/assets`, form);
  },
  /** One of the book's (or its series') images becomes the front cover; answers 204. */
  setCover: (bookId: string, assetId: string) =>
    api.put<undefined>(`${base(bookId)}/cover`, { assetId }),
  /** Back to the generated cover; answers 204. */
  removeCover: (bookId: string) => api.del<undefined>(`${base(bookId)}/cover`),
  duplicate: (bookId: string, title: string) =>
    api.post<DuplicatedBook>(`${base(bookId)}/duplicate`, { title }),
  newEdition: (bookId: string, title: string) =>
    api.post<DuplicatedBook>(`${base(bookId)}/new-edition`, { title }),
  publish: (bookId: string) => api.post<PublishState>(`${base(bookId)}/publish`),
  archive: (bookId: string) => api.post<BookSettings>(`${base(bookId)}/archive`),
  restore: (bookId: string) => api.post<BookSettings>(`${base(bookId)}/restore`),
  exportSource: (bookId: string) => api.post<Job>(`${base(bookId)}/export-source`),
};

/** `GET …/publication-settings`: the Citations & permissions values, each with `inherited`. */
export const getPublicationSettings = (bookId: string, signal?: AbortSignal) =>
  api.get<PublicationSettings>(`${base(bookId)}/publication-settings`, { signal });

/** `GET …/settings/rules-impact?limit=`: the live strip for a limit that may not be saved yet. */
export const getRulesImpact = (bookId: string, limit: number, signal?: AbortSignal) =>
  api.get<RulesImpact>(`${base(bookId)}/settings/rules-impact?limit=${limit}`, { signal });

/** `GET …/settings/layout`: Layout & print, read-only. */
export const getLayoutSettings = (bookId: string, signal?: AbortSignal) =>
  api.get<LayoutSettings>(`${base(bookId)}/settings/layout`, { signal });

/** `GET /api/books/{id}/settings`: every section of the first half of Settings in one request. */
export const getBookSettings = (bookId: string, signal?: AbortSignal) =>
  api.get<BookSettings>(`${base(bookId)}/settings`, { signal });

/** `GET …/publish`: the figures for the Published dialog and the snapshot list. */
export const getPublishState = (bookId: string, signal?: AbortSignal) =>
  api.get<PublishState>(`${base(bookId)}/publish`, { signal });
