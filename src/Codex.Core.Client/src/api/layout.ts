import { api, type RequestOptions } from './client';

// Layout DTOs (C#: Codex.Contracts.Print: LayoutStateDto, LayoutPreviewDto and their parts).
export type LayoutOptionValue = boolean | string;

export interface LayoutOption {
  key: string;
  label: string;
  /** boolean or choice (one of `choices`). */
  type: string;
  choices: string[];
  default: LayoutOptionValue;
  value: LayoutOptionValue;
}

export interface LayoutTemplate {
  slug: string;
  name: string;
  /** The version this book renders the template with: the pinned one, else the newest. */
  version: string;
  engine: string;
  trims: string[];
  columns: number;
  hasPreviewCss: boolean;
  options: LayoutOption[];
  notices: string[];
  templateId?: string | null;
  versionId?: string | null;
  /** The user's own fork or upload (built-ins are false). */
  isUserTemplate?: boolean;
  description?: string | null;
  /** "Fantasy RPG (5e style) v1.0.0" for a fork. */
  forkedFrom?: string | null;
  /** Set only when a newer published version than `version` exists. */
  latestVersion?: string | null;
  latestVersionId?: string | null;
  /** What changed since `version`, newest first. */
  latestChangelog?: string[] | null;
}

export interface LayoutState {
  template: string;
  trim: string;
  templates: LayoutTemplate[];
}

export interface LayoutSelection {
  template: string;
  trim?: string;
  options?: Record<string, LayoutOptionValue>;
}

export type LayoutScope = 'scene' | 'chapter' | 'book';

export interface LayoutPreviewRequest {
  scope: LayoutScope;
  scopeId?: string;
  /** Previews this version of the template (a newer one, before applying it) instead of the pinned one. */
  templateVersionId?: string;
}

/** Kind: raw_latex, unknown_directive, missing_asset, missing_entry or missing_reference. */
export interface LayoutFlag {
  kind: string;
  sceneId: string | null;
  detail: string;
  kept: boolean;
}

/** Status is ok or low (against the 300 dpi print target). */
export interface LayoutFigure {
  figureId: string;
  assetId: string;
  sceneId: string;
  filename: string;
  caption: string | null;
  placement: string;
  widthPx: number | null;
  heightPx: number | null;
  placedWidthIn: number | null;
  placedHeightIn: number | null;
  effectiveDpi: number | null;
  status: string;
}

export interface LayoutPreview {
  html: string;
  css: string | null;
  template: string;
  templateName: string;
  templateVersion: string;
  trim: string;
  options: Record<string, LayoutOptionValue>;
  notices: string[];
  flags: LayoutFlag[];
  figures: LayoutFigure[];
  contentHash: string;
  renderedAt: string;
  templateVersionId?: string | null;
}

export const layoutKey = (bookId: string) => ['layout', bookId] as const;
export const layoutPreviewKey = (bookId: string) => ['layout-preview', bookId] as const;

export const layoutApi = {
  state: (bookId: string, options?: RequestOptions) =>
    api.get<LayoutState>(`/api/books/${bookId}/layout`, options),
  select: (bookId: string, selection: LayoutSelection) =>
    api.put<LayoutState>(`/api/books/${bookId}/layout`, selection),
  preview: (bookId: string, request: LayoutPreviewRequest, options?: RequestOptions) =>
    api.post<LayoutPreview>(`/api/books/${bookId}/layout/preview`, request, options),
};
