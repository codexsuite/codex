import { api, type RequestOptions } from './client';

// Scene prose DTOs (C#: Codex.Contracts.Manuscript). A save from a stale version still saves (last write wins); a block operation from one is ApiRequestError { status: 409, code: 'conflict' }.
export interface Prose {
  sceneId: string;
  body: string;
  /** 0 for a scene with no prose yet. */
  version: number;
  updatedAt: string;
}

export interface SaveProseRequest {
  body: string;
  /** The version the editor loaded. */
  version: number;
}

export interface SaveProseResult {
  version: number;
}

// A scene's version history (C#: ProseHistoryDto, ProseRevisionDto, RestoreRevisionResult).
export interface ProseRevision {
  id: string;
  /** import | manual | autosave | ai_apply | restore | agent | replaced */
  source: string;
  label: string;
  detail: string | null;
  createdAt: string;
  timeLabel: string;
  clockLabel: string;
  words: number;
  body: string;
  current: boolean;
}

export interface ProseHistory {
  sceneId: string;
  archived: boolean;
  version: number;
  currentWords: number;
  /** The scene's text now; not always one of the revisions. */
  body: string;
  revisions: ProseRevision[];
}

export interface RestoreRevisionResult {
  version: number;
  body: string;
  previousRevisionId: string;
}

export const manuscriptApi = {
  history: (sceneId: string, options?: RequestOptions) =>
    api.get<ProseHistory>(`/api/scenes/${sceneId}/prose/revisions`, options),
  restoreRevision: (sceneId: string, revisionId: string) =>
    api.post<RestoreRevisionResult>(`/api/scenes/${sceneId}/prose/revisions/${revisionId}/restore`),
  prose: (sceneId: string, options?: RequestOptions) =>
    api.get<Prose>(`/api/scenes/${sceneId}/prose`, options),
  saveProse: (sceneId: string, request: SaveProseRequest) =>
    api.put<SaveProseResult>(`/api/scenes/${sceneId}/prose`, request),
};
