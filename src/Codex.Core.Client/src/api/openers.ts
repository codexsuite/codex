import { api } from './client';

// C#: Codex.Contracts.Print.SetOpenerRequest / OpenerDto (`PUT /api/acts/{id}/opener`, `PUT /api/chapters/{id}/opener`).
export type OpenerKind = 'act' | 'chapter';

/** assetId null clears the opener and its caption. */
export interface SetOpenerRequest {
  assetId: string | null;
  caption?: string | null;
}

export interface Opener {
  id: string;
  kind: OpenerKind;
  assetId: string | null;
  caption: string | null;
}

export const openerApi = {
  /** The act's part-opener or the chapter's opener art. 400 for an image outside the book and its series. */
  set: (kind: OpenerKind, id: string, request: SetOpenerRequest) =>
    api.put<Opener>(`/api/${kind === 'act' ? 'acts' : 'chapters'}/${id}/opener`, request),
};
