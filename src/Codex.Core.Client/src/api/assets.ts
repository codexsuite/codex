import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.Print.AssetDto, AssetRenditionDto, AssetDpiDto (`/api/books/{id}/assets`, `/api/assets/{id}/…`).

/** Where a figure sits on the page; an opener is drawn as a full page. */
export type Placement = 'column' | 'page' | 'spread' | 'float-top';

export const placements: readonly Placement[] = ['column', 'page', 'spread', 'float-top'];

export const placementLabels: Record<Placement, string> = {
  column: 'Column',
  page: 'Full page',
  spread: 'Spread',
  'float-top': 'Top of page',
};

/** Effective resolution where the image is placed; status is ok or low against the target (300 print). */
export interface AssetDpi {
  trim: string;
  columns: number;
  placement: string;
  placedWidthIn: number;
  placedHeightIn: number;
  effectiveDpi: number;
  targetDpi: number;
  status: 'ok' | 'low';
}

export interface AssetRendition {
  /** web | proxy | print */
  purpose: string;
  mime: string;
  widthPx: number;
  heightPx: number;
  bytes: number;
  url: string;
}

export interface Asset {
  id: string;
  bookId: string | null;
  /** "book" for the book's own image, "series" for one shared from its series. */
  scope: string;
  kind: string;
  filename: string;
  mime: string;
  bytes: number;
  widthPx: number | null;
  heightPx: number | null;
  /** processing | ready | failed | none (a seed row never queued). */
  status: string;
  error: string | null;
  url: string;
  renditions: AssetRendition[];
  dpi: AssetDpi | null;
  createdAt: string;
  createdLabel: string;
}

/** The layout an image's dpi is judged for: the book's trim and its template's columns. */
export interface DpiQuery {
  trim: string;
  columns: number;
  placement: Placement;
}

export const bookImagesKey = (bookId: string) => ['book-images', bookId] as const;

export const webRendition = (asset: Asset): string | null =>
  asset.renditions.find((r) => r.purpose === 'web')?.url ?? null;

export const assetApi = {
  /** The book's and its series' images, newest first; with `dpi`, each carries its effective dpi there. */
  list: (bookId: string, dpi: DpiQuery | null, options?: RequestOptions) => {
    const query =
      dpi === null
        ? ''
        : `?${new URLSearchParams({
            trim: dpi.trim,
            columns: String(dpi.columns),
            placement: dpi.placement,
          }).toString()}`;
    return api.get<Asset[]>(`/api/books/${encodeURIComponent(bookId)}/assets${query}`, options);
  },
  /** Stores a PNG, JPEG or WebP image (25 MB at most); its renditions are made in the background. */
  upload: (bookId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api.upload<Asset>(`/api/books/${encodeURIComponent(bookId)}/assets`, form);
  },
};
