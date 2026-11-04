import { api } from '@codex/core/api/client';

// C#: Codex.Contracts.Citations. Kind (quote, paraphrase, data, figure, lyric) defaults to paraphrase on the server.
export interface CitePassageRequest {
  sceneId: string;
  sourceId: string;
  version: number;
  passage: string;
  occurrence?: number;
  kind?: string;
  locator?: string;
}

export interface CitePassageResult {
  useId: string;
  sceneId: string;
  version: number;
  body: string;
  number: number;
  markersShifted: number;
  statusBefore: string;
  status: string;
  reason: string;
}

export interface RemoveCitationResult {
  useId: string;
  sceneId: string;
  version: number;
  body: string;
  markersShifted: number;
  status: string;
}

export const citationsApi = {
  /** Wraps the passage as a source marker and adds its use; 409 stale version, 400 a passage that is not there. */
  cite: (request: CitePassageRequest) => api.post<CitePassageResult>('/api/citations', request),
  /** Unwraps the marker and archives its use; 400 for a marker in a pending AI draft. */
  remove: (useId: string, version: number) =>
    api.post<RemoveCitationResult>(`/api/citations/${useId}/remove`, { version }),
  /**
   * Cites a possible unflagged quote: its words in the scene's current prose become a quote marker for the flag's
   * guessed source (or `sourceId`) and the flag is resolved. 409 already resolved, 400 no source.
   */
  citeFlag: (flagId: string, sourceId?: string) =>
    api.post<CitePassageResult>(`/api/quote-flags/${flagId}/cite`, { sourceId: sourceId ?? null }),
};
