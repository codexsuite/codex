import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiRequestError } from '@codex/core/api/client';
import { beatsKey, blockApi, setSceneProse } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { writeLink } from '@codex/core/shell/links';
import { useDeepLink } from '../../shell/useDeepLink';
import { resolveTarget, type ActView } from '@codex/core/features/write/manuscriptView';
import { paraphraseInstruction, withParaphraseBeat } from './paraphraseBeat';
import { useSaveCoordinator } from './saveCoordinator';
import { useSceneRegistry } from './sceneRegistry';
import { useMarkers } from './useMarkers';

// References' Paraphrase with AI lands here with `#scene=…&ref=<source>&paraphrase=1` (J10 path 1). Once the scene's
// editor is up: a Scene beat goes straight after the quote, its instruction asking for the quoted words in the
// author's own words. The author generates and applies the beat's draft, replaces the quote with it and only then
// changes the use's type to Paraphrase (References' use chip): while the verbatim words are in the prose they still
// count as quoted. The request is used once: the link becomes the scene and marker link at once. Draws nothing.
export function PreparedParaphrase({ acts }: { acts: readonly ActView[] }) {
  const { bookId = '' } = useParams();
  const deepLink = useDeepLink();
  const registry = useSceneRegistry();
  const coordinator = useSaveCoordinator();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { markers } = useMarkers(bookId);
  const handled = useRef<string | null>(null);

  const link = deepLink.ok ? deepLink.link : null;
  const wanted = link?.paraphrase === true ? link.ref : undefined;
  const scene = link?.scene;
  const target = link ? resolveTarget(acts, link) : null;
  const sceneId = target?.kind === 'scene' ? target.id : undefined;
  const marker = markers?.find(
    (m) => m.sourceId === wanted && m.sceneId === sceneId && m.generationId === null,
  );

  useEffect(() => {
    if (wanted === undefined || sceneId === undefined || markers === undefined) return;
    const key = `${sceneId}|${wanted}`;
    if (handled.current === key) return;
    handled.current = key;
    void navigate(writeLink(bookId, { scene, ref: wanted }), { replace: true });
    if (!marker) {
      showToast({ message: 'That source has no marker in this scene to paraphrase.' });
      return;
    }
    const failed = (error: unknown, fallback: string) =>
      error instanceof Error ? error.message : fallback;
    void (async () => {
      const handle = await registry.whenRegistered(sceneId);
      if (!handle) {
        showToast({
          message: `Scene ${scene ?? ''} is not shown in Write, so no paraphrase beat was added. Show the whole book and try again.`,
        });
        return;
      }
      let added: { beatId: string; body: string; version: number; words: string };
      try {
        if (!(await handle.settle())) return;
        const saved = handle.saved();
        const beatId = crypto.randomUUID();
        const prepared = withParaphraseBeat(saved.body, marker.useId, beatId);
        if (!prepared) {
          showToast({ message: 'The quote is not in the scene’s prose any more.' });
          return;
        }
        const result = await blockApi.insertBeat(sceneId, {
          beatId,
          body: prepared.body,
          version: saved.version,
        });
        added = { beatId, body: prepared.body, version: result.version, words: prepared.words };
      } catch (error) {
        if (error instanceof ApiRequestError && error.status === 409) {
          coordinator.store(sceneId).set('conflict');
        }
        showToast({
          message: `No paraphrase beat was added: ${failed(error, 'the scene could not be saved.')}`,
        });
        return;
      }
      setSceneProse(queryClient, bookId, { sceneId, body: added.body, version: added.version });
      try {
        await blockApi.patchBeat(added.beatId, {
          body: paraphraseInstruction(marker.sourceTitle, added.words),
        });
        showToast({
          message: `A beat to paraphrase ${marker.sourceTitle} is ready under the quote. Generate, apply, then set the use to Paraphrase.`,
        });
      } catch (error) {
        showToast({
          message: `The beat was added under the quote, but its instruction was not saved (${failed(error, 'the request failed')}). Write it in the beat.`,
        });
      } finally {
        await queryClient.invalidateQueries({ queryKey: beatsKey(bookId) });
      }
    })();
  }, [
    bookId,
    wanted,
    scene,
    sceneId,
    marker,
    markers,
    navigate,
    registry,
    coordinator,
    queryClient,
  ]);

  return null;
}
