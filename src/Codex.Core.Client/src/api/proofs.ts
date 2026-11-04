import { api, type RequestOptions } from './client';
import type { LayoutOptionValue } from './layout';

// Proof DTOs (C#: Codex.Contracts.Print: ProofDto, ProofRequest, ProofListDto).
export type ProofStatus = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

export interface Proof {
  id: string;
  chapterId: string;
  status: ProofStatus;
  step: string | null;
  /** 0 to 1 while it runs. */
  progress: number | null;
  cacheKey: string;
  templateSlug: string;
  templateVersion: string;
  trim: string;
  pageCount: number | null;
  /** One line: why a failed proof failed. */
  error: string | null;
  /** pandoc's then Tectonic's output; on the single-proof read only. */
  log: string | null;
  /** True when the request reused a finished or running proof instead of queuing a new one. */
  cached: boolean;
  pdfUrl: string | null;
  createdAt: string;
  finishedAt: string | null;
}

export interface ProofList {
  /** The key a proof of the chapter as it is now would have: another key on the newest proof means out of date. */
  currentCacheKey: string;
  proofs: Proof[];
}

export interface ProofRequest {
  chapterId: string;
  template?: string;
  trim?: string;
  options?: Record<string, LayoutOptionValue>;
  rerender?: boolean;
}

export const proofListKey = (bookId: string) => ['layout-proofs', bookId] as const;
export const proofKey = (proofId: string | null) => ['layout-proof', proofId] as const;

export const proofApi = {
  request: (bookId: string, request: ProofRequest) =>
    api.post<Proof>(`/api/books/${bookId}/proofs`, request),
  list: (bookId: string, chapterId: string, options?: RequestOptions) =>
    api.get<ProofList>(`/api/books/${bookId}/proofs?chapterId=${chapterId}`, options),
  get: (proofId: string, options?: RequestOptions) =>
    api.get<Proof>(`/api/proofs/${proofId}`, options),
};
