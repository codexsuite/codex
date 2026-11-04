import type { Page, Route } from 'playwright';
import type { Asset } from '@codex/core/api/assets';
import type { LayoutState } from '@codex/core/api/layout';

// Shared by the image picker's component specs (ImagePicker, Write's figure dialogs, Plan's opener dialog): a book in
// a two-column Letter layout with two images, one low-resolution and ready, one still being prepared.

export const PICKER_BOOK = '0199a000-0000-7000-8000-000000000012';
export const READY_ASSET = '0199a000-0000-7000-8000-0000000000f1';
export const PREPARING_ASSET = '0199a000-0000-7000-8000-0000000000f2';
export const UPLOADED_ASSET = '0199a000-0000-7000-8000-0000000000f3';

export const pickerLayout: LayoutState = {
  template: 'fantasy-rpg',
  trim: 'letter',
  templates: [
    {
      slug: 'fantasy-rpg',
      name: 'Fantasy RPG',
      version: '1.0.0',
      engine: 'pdflatex',
      trims: ['letter'],
      columns: 2,
      hasPreviewCss: true,
      options: [],
      notices: [],
    },
  ],
};

export function asset(id: string, overrides: Partial<Asset> = {}): Asset {
  return {
    id,
    bookId: PICKER_BOOK,
    scope: 'book',
    kind: 'image',
    filename: 'map.png',
    mime: 'image/png',
    bytes: 1000,
    widthPx: 480,
    heightPx: 320,
    status: 'ready',
    error: null,
    url: `/api/assets/${id}`,
    renditions: [
      {
        purpose: 'web',
        mime: 'image/webp',
        widthPx: 480,
        heightPx: 320,
        bytes: 500,
        url: `/api/assets/${id}/renditions/web`,
      },
    ],
    dpi: {
      trim: 'letter',
      columns: 2,
      placement: 'column',
      placedWidthIn: 3.4,
      placedHeightIn: 2.27,
      effectiveDpi: 141.2,
      targetDpi: 300,
      status: 'low',
    },
    createdAt: '2026-10-20T10:00:00+00:00',
    createdLabel: '2d ago',
    ...overrides,
  };
}

export const pickerAssets: Asset[] = [
  asset(READY_ASSET, { filename: 'Fallen World.png' }),
  asset(PREPARING_ASSET, {
    filename: 'Kharbad Spine.png',
    status: 'processing',
    renditions: [],
    widthPx: 3000,
    heightPx: 2000,
    dpi: { ...asset(READY_ASSET).dpi!, effectiveDpi: 882.4, status: 'ok' },
  }),
];

const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAHnOcQAAAAABJRU5ErkJggg==',
  'base64',
);

export interface PickerCalls {
  /** The query string of each list request. */
  lists: string[];
  uploads: number;
}

/**
 * Stubs the book's layout state, its image list (`list` is read on every request, so a test can change it) and the
 * web renditions. An upload answers `upload` and, when it succeeds, adds it to the list.
 */
export async function stubPicker(
  page: Page,
  {
    bookId = PICKER_BOOK,
    list = [...pickerAssets],
    upload = {
      status: 201,
      json: asset(UPLOADED_ASSET, { filename: 'new.png', status: 'processing', renditions: [] }),
    },
  }: { bookId?: string; list?: Asset[]; upload?: { status: number; json: unknown } } = {},
): Promise<PickerCalls> {
  const calls: PickerCalls = { lists: [], uploads: 0 };
  await page.route(`**/api/books/${bookId}/layout`, (route) =>
    route.fulfill({ json: pickerLayout }),
  );
  await page.route(`**/api/books/${bookId}/assets**`, (route: Route) => {
    if (route.request().method() === 'POST') {
      calls.uploads += 1;
      if (upload.status < 300) list.unshift(upload.json as Asset);
      return route.fulfill(upload);
    }
    calls.lists.push(new URL(route.request().url()).search);
    return route.fulfill({ json: list });
  });
  await page.route('**/api/assets/*/renditions/web', (route) =>
    route.fulfill({ contentType: 'image/png', body: PNG }),
  );
  return calls;
}

export const pngFile = { name: 'new.png', mimeType: 'image/png', buffer: PNG };
