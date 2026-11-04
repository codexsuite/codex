import { ApiRequestError } from '@codex/core/api/client';
import { invalidateOutline } from '@codex/core/api/outline';
import {
  addSceneProse,
  beatsKey,
  blockApi,
  setSceneProse,
  type SplitSceneResult,
} from '@codex/core/api/write';
import type { QueryClient } from '@tanstack/react-query';
import type { SceneSaves } from './sceneSaves';

// The two block operations the editor page asks for (accessory bar: Beat, new scene). Only the page has the cursor, so it
// sends the finished text; this adds the scene's saved version and calls the same endpoints the desktop's `useBlockOps` does.
// Nothing is applied first: a refused call leaves the page as it was.

export interface BlockDeps {
  queryClient: QueryClient;
  bookId: string;
  saves: SceneSaves;
  /** The scenes of the open chapter: a split settles all of them before the chapter is loaded again. */
  sceneIds: readonly string[];
}

export type BlockOutcome =
  | { status: 'done'; newSceneId?: string }
  /** `stale`: the server refused the scene's version (409), so the page's copy is out of date. */
  | { status: 'failed'; message: string; stale: boolean };

const NOT_SAVED = 'Your changes could not be saved yet. Try again in a moment.';
const FALLBACK = 'The change could not be saved.';

async function guarded(
  deps: BlockDeps,
  sceneId: string,
  settleIds: readonly string[],
  work: (saved: { body: string; version: number }) => Promise<BlockOutcome>,
): Promise<BlockOutcome> {
  try {
    // Like the desktop, settle the pending autosaves first, or the call 409s on a stale version.
    const settled = await deps.saves.settle(settleIds);
    const saved = deps.saves.saved(sceneId);
    if (!settled || !saved) return { status: 'failed', message: NOT_SAVED, stale: false };
    return await work(saved);
  } catch (error) {
    const stale = error instanceof ApiRequestError && error.status === 409;
    if (stale) deps.saves.markConflict(sceneId);
    return {
      status: 'failed',
      message: error instanceof Error ? error.message : FALLBACK,
      stale,
    };
  }
}

/** Insert a beat: `body` is the scene Markdown with `::beat{id="beatId"}` at the cursor. */
export function insertBeat(
  deps: BlockDeps,
  sceneId: string,
  beatId: string,
  body: string,
): Promise<BlockOutcome> {
  return guarded(deps, sceneId, [sceneId], async ({ version }) => {
    const result = await blockApi.insertBeat(sceneId, { beatId, body, version });
    await deps.queryClient.invalidateQueries({ queryKey: beatsKey(deps.bookId) });
    setSceneProse(deps.queryClient, deps.bookId, { sceneId, body, version: result.version });
    deps.saves.reset(sceneId, body, result.version);
    return { status: 'done' };
  });
}

/** Split a scene at the cursor: the Markdown before and after it. */
export function splitScene(
  deps: BlockDeps,
  sceneId: string,
  headBody: string,
  tailBody: string,
): Promise<BlockOutcome> {
  return guarded(deps, sceneId, deps.sceneIds, async ({ version }) => {
    const result: SplitSceneResult = await blockApi.splitScene(sceneId, {
      headBody,
      tailBody,
      version,
    });
    addSceneProse(deps.queryClient, deps.bookId, {
      sceneId: result.newSceneId,
      body: tailBody,
      version: result.newSceneVersion,
    });
    setSceneProse(deps.queryClient, deps.bookId, {
      sceneId,
      body: headBody,
      version: result.version,
    });
    await invalidateOutline(deps.queryClient, deps.bookId);
    await deps.queryClient.invalidateQueries({ queryKey: beatsKey(deps.bookId) });
    return { status: 'done', newSceneId: result.newSceneId };
  });
}
