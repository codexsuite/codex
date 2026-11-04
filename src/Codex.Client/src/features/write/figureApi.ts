import { useQuery } from '@tanstack/react-query';
import type { Placement } from '@codex/core/api/assets';
import { api, type RequestOptions } from '@codex/core/api/client';

// C#: Codex.Contracts.Print.FigureDto / AssetDpiDto, CreateFigureRequest, PatchFigureRequest, RemoveFigureRequest.
export interface FigureDpi {
  effectiveDpi: number;
  targetDpi: number;
  status: 'ok' | 'low';
}

export interface Figure {
  id: string;
  sceneId: string;
  assetId: string;
  placement: string;
  caption: string | null;
  altText: string | null;
  missing: boolean;
  webUrl: string | null;
  widthPx: number | null;
  heightPx: number | null;
  dpi: FigureDpi | null;
}

/** What the Insert figure and Edit figure dialogs set. */
export interface FigureFields {
  assetId: string;
  placement: Placement;
  caption: string;
  altText: string;
}

/** The client makes the figure's id and sends the whole new body holding its `::figure` line. */
export interface CreateFigureRequest {
  figureId: string;
  assetId: string;
  body: string;
  version: number;
  placement: Placement;
  caption: string | null;
  altText: string | null;
}

export interface CreateFigureResult {
  figureId: string;
  version: number;
}

/** A field left out is unchanged; an empty caption or alt text clears it. */
export interface PatchFigureRequest {
  assetId?: string;
  placement?: Placement;
  caption?: string;
  altText?: string;
}

export interface RemoveFigureResult {
  sceneId: string;
  version: number;
}

export const figureKey = (figureId: string) => ['figure', figureId] as const;

export const figureApi = {
  get: (figureId: string, options?: RequestOptions) =>
    api.get<Figure>(`/api/figures/${figureId}`, options),
  /** 409 when `version` is stale; 400 when the body lacks the line or drops another embed. */
  create: (sceneId: string, request: CreateFigureRequest) =>
    api.post<CreateFigureResult>(`/api/scenes/${sceneId}/figures`, request),
  patch: (figureId: string, request: PatchFigureRequest) =>
    api.patch<Figure>(`/api/figures/${figureId}`, request),
  /** Takes the figure's line out of the scene saved at `version`, which archives the figure. */
  remove: (figureId: string, version: number) =>
    api.post<RemoveFigureResult>(`/api/figures/${figureId}/remove`, { version }),
};

/** Read again every two seconds while the image's web rendition is still being made. */
export function useFigure(figureId: string) {
  return useQuery({
    queryKey: figureKey(figureId),
    queryFn: ({ signal }) => figureApi.get(figureId, { signal }),
    refetchInterval: (query) => {
      const figure = query.state.data;
      return figure !== undefined && !figure.missing && figure.webUrl === null ? 2000 : false;
    },
  });
}
