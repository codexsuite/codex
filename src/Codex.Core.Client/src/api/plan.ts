import { api } from './client';
import { outlineKey } from './outline';
import type { Outline, OutlineAct, OutlineChapter, OutlineScene } from './structure';

// The Plan lane's writes. The outline itself is the shared `useOutline` (api/outline.ts); Plan writes invalidate it
// (which also covers the archived-scene lists nested under its key) or replace it with the outline a move answers with.
export const archivedScenesKey = (bookId: string, chapterId: string) =>
  [...outlineKey(bookId), 'archived', chapterId] as const;

// C#: Codex.Contracts.Structure. `afterId` null puts the item first.
export interface SceneUse {
  useId: string;
  sourceId: string;
  sourceTitle: string;
}

export interface ArchiveSceneResult {
  sceneId: string;
  archivedUses: SceneUse[];
}

export interface RestoreSceneResult {
  sceneId: string;
  restoredUses: SceneUse[];
}

export interface ArchiveActResult {
  actId: string;
  archivedUses: SceneUse[];
}

export interface ArchiveChapterResult {
  chapterId: string;
  sceneCount: number;
  archivedUses: SceneUse[];
}

export interface MergeSceneResult {
  sceneId: string;
  version: number;
  archivedSceneId: string;
}

export interface ArchivedScene {
  id: string;
  /** Null for an unnumbered chapter. */
  chapterNumber: number | null;
  wasSceneNumber: number;
  title: string | null;
  /** Markdown. */
  summary: string | null;
  words: number;
  useCount: number;
}

export interface CreateActRequest {
  /** Null or empty leaves the act untitled ("Name this act"). */
  title: string | null;
  /** Null appends to the end of the book. */
  afterId: string | null;
}

export interface CreateChapterRequest {
  title: string | null;
  /** Null appends to the end of the act. */
  afterId: string | null;
}

export interface CreateSceneRequest {
  title: string | null;
  /** Null appends to the end of the chapter. */
  afterId: string | null;
}

export const planApi = {
  /** The new act holds one chapter with one empty scene. */
  createAct: (bookId: string, request: CreateActRequest) =>
    api.post<OutlineAct>(`/api/books/${bookId}/acts`, request),
  renameAct: (actId: string, title: string) =>
    api.patch<OutlineAct>(`/api/acts/${actId}`, { title }),
  archiveAct: (actId: string) => api.post<ArchiveActResult>(`/api/acts/${actId}/archive`),
  restoreAct: (actId: string) => api.post<OutlineAct>(`/api/acts/${actId}/restore`),
  createChapter: (actId: string, request: CreateChapterRequest) =>
    api.post<OutlineChapter>(`/api/acts/${actId}/chapters`, request),
  createScene: (chapterId: string, request: CreateSceneRequest) =>
    api.post<OutlineScene>(`/api/chapters/${chapterId}/scenes`, request),
  moveAct: (actId: string, afterId: string | null) =>
    api.put<Outline>(`/api/acts/${actId}/position`, { afterId }),
  moveChapter: (chapterId: string, actId: string, afterId: string | null) =>
    api.put<Outline>(`/api/chapters/${chapterId}/position`, { actId, afterId }),
  moveScene: (sceneId: string, chapterId: string, afterId: string | null) =>
    api.put<Outline>(`/api/scenes/${sceneId}/position`, { chapterId, afterId }),
  renameChapter: (chapterId: string, title: string) =>
    api.patch<OutlineChapter>(`/api/chapters/${chapterId}`, { title }),
  archiveChapter: (chapterId: string) =>
    api.post<ArchiveChapterResult>(`/api/chapters/${chapterId}/archive`),
  restoreChapter: (chapterId: string) =>
    api.post<OutlineChapter>(`/api/chapters/${chapterId}/restore`),
  mergeScene: (sceneId: string) =>
    api.post<MergeSceneResult>(`/api/scenes/${sceneId}/merge-previous`),
  archiveScene: (sceneId: string) => api.post<ArchiveSceneResult>(`/api/scenes/${sceneId}/archive`),
  restoreScene: (sceneId: string) => api.post<RestoreSceneResult>(`/api/scenes/${sceneId}/restore`),
  archivedScenes: (chapterId: string) =>
    api.get<ArchivedScene[]>(`/api/chapters/${chapterId}/archived-scenes`),
};
