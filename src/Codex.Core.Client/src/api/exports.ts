import { api, type RequestOptions } from './client';
import type { LayoutOptionValue } from './layout';

// Export for printer DTOs (C#: Codex.Contracts.Print: ExportRequest, ExportDto, ExportListDto, PreflightDto, PreflightItemDto).
export type ExportBleed = 'none' | '3mm' | '0.125in';
export type ExportQuality = 'print' | 'screen';

export interface ExportRequest {
  template?: string;
  trim?: string;
  options?: Record<string, LayoutOptionValue>;
  bleed: ExportBleed;
  cropMarks: boolean;
  quality: ExportQuality;
  format: 'pdf';
  draft: boolean;
}

/** severity: block, warn or note. kind: low_dpi, raw_latex, unknown_directive, missing_asset, missing_entry, missing_reference or copyright_risk. */
export interface PreflightItem {
  severity: string;
  kind: string;
  detail: string;
  sceneId: string | null;
  referenceId: string | null;
}

export interface Preflight {
  /** `blocked` or `ready`. */
  verdict: string;
  blocks: PreflightItem[];
  warnings: PreflightItem[];
  notes: PreflightItem[];
  template: string;
  templateVersion: string;
  trim: string;
  estimatedMegabytes: number;
}

export type ExportStatus = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

export interface ExportJob {
  id: string;
  /** print, source, docx, markdown or epub. */
  kind: string;
  status: ExportStatus;
  step: string | null;
  /** 0 to 1 while it runs. */
  progress: number | null;
  template: string | null;
  templateVersion: string | null;
  trim: string | null;
  bleed: ExportBleed | null;
  cropMarks: boolean;
  quality: ExportQuality | null;
  draft: boolean;
  pageCount: number | null;
  bytes: number | null;
  error: string | null;
  /** On the single-export read only. */
  log: string | null;
  blocked: number;
  warnings: number;
  overfull: string[];
  fileUrl: string | null;
  createdAt: string;
  createdLabel: string;
  finishedAt: string | null;
  /** The file's name (a source or manuscript export that succeeded). */
  filename?: string | null;
  /** What a manuscript or source export left out ("1 raw-LaTeX block skipped"). */
  notes?: string[];
}

export interface ExportList {
  /** The print export that is queued or running now. */
  running: string | null;
  exports: ExportJob[];
}

export const DEFAULT_EXPORT: ExportRequest = {
  bleed: 'none',
  cropMarks: false,
  quality: 'print',
  format: 'pdf',
  draft: false,
};

export const exportsKey = (bookId: string) => ['layout-exports', bookId] as const;
export const exportKey = (exportId: string | null) => ['layout-export', exportId] as const;
export const preflightKey = (bookId: string) => ['layout-preflight', bookId] as const;

export const exportApi = {
  preflight: (bookId: string, request: ExportRequest, options?: RequestOptions) =>
    api.post<Preflight>(`/api/books/${bookId}/export/preflight`, request, options),
  request: (bookId: string, request: ExportRequest) =>
    api.post<ExportJob>(`/api/books/${bookId}/exports`, request),
  list: (bookId: string, options?: RequestOptions) =>
    api.get<ExportList>(`/api/books/${bookId}/exports`, options),
  get: (exportId: string, options?: RequestOptions) =>
    api.get<ExportJob>(`/api/exports/${exportId}`, options),
  cancel: (exportId: string) => api.post<ExportJob>(`/api/exports/${exportId}/cancel`),
};

// Export manuscript (C#: Codex.Contracts.Print: ManuscriptExportRequest, ManuscriptExportInfoDto, ManuscriptChapterDto).
export type ManuscriptFormat = 'docx' | 'markdown' | 'epub';

export interface ManuscriptExportRequest {
  format: ManuscriptFormat;
  scope: 'book' | 'chapter';
  chapterId?: string;
  includeBeats: boolean;
  includeSummaries: boolean;
}

export interface ManuscriptChapter {
  id: string;
  title: string;
  /** Null for front matter. */
  number: number | null;
}

export interface ManuscriptExportInfo {
  chapters: ManuscriptChapter[];
  atRiskSources: number;
}

export const manuscriptExportsKey = (bookId: string) => ['manuscript-exports', bookId] as const;
export const manuscriptInfoKey = (bookId: string) => ['manuscript-export-info', bookId] as const;

export const manuscriptExportApi = {
  info: (bookId: string, options?: RequestOptions) =>
    api.get<ManuscriptExportInfo>(`/api/books/${bookId}/manuscript-export`, options),
  request: (bookId: string, request: ManuscriptExportRequest) =>
    api.post<ExportJob>(`/api/books/${bookId}/manuscript-exports`, request),
  list: (bookId: string, options?: RequestOptions) =>
    api.get<ExportList>(`/api/books/${bookId}/manuscript-exports`, options),
};
