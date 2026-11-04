import { useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { Editor } from '@tiptap/react';
import { useParams } from 'react-router-dom';
import { ApiRequestError } from '@codex/core/api/client';
import { manuscriptApi } from '@codex/core/api/manuscript';
import { invalidateOutline } from '@codex/core/api/outline';
import {
  additionApi,
  additionsKey,
  addSceneProse,
  beatsKey,
  blockApi,
  setSceneProse,
} from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { parse, serialize } from '../../prose';
import { bodyOf, extractUnit, nodes, splitBody, stepTarget, withUnit, type Step } from './blockOps';
import { figureApi, figureKey, type FigureFields } from './figureApi';
import { useSaveCoordinator } from './saveCoordinator';
import { useSceneRegistry } from './sceneRegistry';

export interface BlockOps {
  insertBeat(sceneId: string, editor: Editor): Promise<void>;
  /** A Codex addition for `entryId` at the caret: the `::kb-addition` line and its row, saved together. */
  insertAddition(sceneId: string, editor: Editor, entryId: string): Promise<void>;
  /** A figure at the caret: the `::figure` line and its row, saved together. */
  insertFigure(sceneId: string, editor: Editor, figure: FigureFields): Promise<void>;
  /** Takes the figure's line out (the server archives the figure); Undo puts the line back, which revives it. */
  removeFigure(figureId: string, sceneId: string): Promise<void>;
  splitScene(sceneId: string, editor: Editor): Promise<void>;
  /** Moves a beat and its prose; `index` is a block position in the target body as it is now. */
  moveBeat(beatId: string, fromSceneId: string, toSceneId: string, index: number): Promise<void>;
  stepBeat(beatId: string, sceneId: string, direction: 'up' | 'down'): Promise<void>;
  /** Removes the beat's line (the server archives the beat), and its prose too unless `keepProse`; Undo restores. */
  deleteBeat(beatId: string, sceneId: string, keepProse: boolean): Promise<void>;
}

const sceneOrder = (): string[] =>
  [...document.querySelectorAll<HTMLElement>('[data-scene-id]')].map((e) => e.dataset.sceneId!);

// The structural edits of a scene body: each settles the scenes it touches, sends whole new bodies with their
// versions, and writes the answer to the cache, which remounts the editors on the server's copy. Nothing is applied
// first, so a refused call (stale version, 400) leaves the screen as it was.
export function useBlockOps(): BlockOps {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const coordinator = useSaveCoordinator();
  const registry = useSceneRegistry();

  return useMemo(() => {
    const settled = async (sceneIds: string[]) => {
      for (const id of sceneIds) {
        if (!(await registry.get(id)?.settle())) return false;
      }
      return true;
    };
    const guarded = async (sceneIds: string[], work: () => Promise<void>) => {
      try {
        if (await settled(sceneIds)) await work();
      } catch (error) {
        if (error instanceof ApiRequestError && error.status === 409) {
          for (const id of sceneIds) coordinator.store(id).set('conflict');
          return;
        }
        showToast({
          message: error instanceof Error ? error.message : 'The change could not be saved.',
        });
      }
    };
    const savedOf = (id: string) => registry.get(id)!.saved();
    const refreshBeats = () => queryClient.invalidateQueries({ queryKey: beatsKey(bookId) });

    const move = (beatId: string, fromId: string, toId: string, index: number, inRest: boolean) =>
      guarded(fromId === toId ? [fromId] : [fromId, toId], async () => {
        const from = savedOf(fromId);
        const extracted = extractUnit(from.body, beatId);
        if (!extracted) return;
        if (fromId === toId) {
          const inside =
            !inRest && index >= extracted.at && index <= extracted.at + extracted.unit.length;
          const at = inside
            ? extracted.at
            : !inRest && index > extracted.at
              ? index - extracted.unit.length
              : index;
          const body = withUnit(extracted.rest, extracted.unit, at, extracted.like);
          if (body === from.body) return;
          const saved = await manuscriptApi.saveProse(fromId, { body, version: from.version });
          setSceneProse(queryClient, bookId, { sceneId: fromId, body, version: saved.version });
          return;
        }
        const to = savedOf(toId);
        const target = parse(to.body, 'prose');
        const toBody = withUnit(nodes(target), extracted.unit, index, target);
        const fromBody = bodyOf(extracted.rest, extracted.like);
        const result = await blockApi.moveBlock({
          fromSceneId: fromId,
          fromVersion: from.version,
          fromBody,
          toSceneId: toId,
          toVersion: to.version,
          toBody,
        });
        await refreshBeats();
        setSceneProse(queryClient, bookId, {
          sceneId: fromId,
          body: fromBody,
          version: result.fromVersion,
        });
        setSceneProse(queryClient, bookId, {
          sceneId: toId,
          body: toBody,
          version: result.toVersion,
        });
      });

    return {
      insertBeat: (sceneId, editor) =>
        guarded([sceneId], async () => {
          const beatId = crypto.randomUUID();
          const node = editor.schema.nodes.directive!.create({ name: 'beat', ref: beatId });
          const body = serialize(editor.state.tr.replaceSelectionWith(node, false).doc);
          const { version } = savedOf(sceneId);
          const result = await blockApi.insertBeat(sceneId, { beatId, body, version });
          await refreshBeats();
          setSceneProse(queryClient, bookId, { sceneId, body, version: result.version });
        }),
      insertAddition: (sceneId, editor, entryId) =>
        guarded([sceneId], async () => {
          const additionId = crypto.randomUUID();
          const node = editor.schema.nodes.directive!.create({
            name: 'kb-addition',
            ref: additionId,
          });
          const sceneBody = serialize(editor.state.tr.replaceSelectionWith(node, false).doc);
          const { version } = savedOf(sceneId);
          const result = await additionApi.insert(sceneId, {
            additionId,
            entryId,
            body: null,
            sceneBody,
            version,
          });
          await queryClient.invalidateQueries({ queryKey: additionsKey(bookId) });
          setSceneProse(queryClient, bookId, { sceneId, body: sceneBody, version: result.version });
        }),
      insertFigure: (sceneId, editor, figure) =>
        guarded([sceneId], async () => {
          const figureId = crypto.randomUUID();
          const node = editor.schema.nodes.directive!.create({ name: 'figure', ref: figureId });
          const body = serialize(editor.state.tr.replaceSelectionWith(node, false).doc);
          const { version } = savedOf(sceneId);
          const result = await figureApi.create(sceneId, {
            figureId,
            assetId: figure.assetId,
            body,
            version,
            placement: figure.placement,
            caption: figure.caption.trim() === '' ? null : figure.caption,
            altText: figure.altText.trim() === '' ? null : figure.altText,
          });
          setSceneProse(queryClient, bookId, { sceneId, body, version: result.version });
        }),
      removeFigure: (figureId, sceneId) =>
        guarded([sceneId], async () => {
          const before = savedOf(sceneId);
          await figureApi.remove(figureId, before.version);
          // The server took the line out; read its body back rather than guess its spacing.
          const after = await manuscriptApi.prose(sceneId);
          setSceneProse(queryClient, bookId, { sceneId, body: after.body, version: after.version });
          showToast({
            message: 'Figure removed',
            undo: () =>
              guarded([sceneId], async () => {
                const restored = await manuscriptApi.saveProse(sceneId, {
                  body: before.body,
                  version: savedOf(sceneId).version,
                });
                await queryClient.invalidateQueries({ queryKey: figureKey(figureId) });
                setSceneProse(queryClient, bookId, {
                  sceneId,
                  body: before.body,
                  version: restored.version,
                });
              }),
          });
        }),
      splitScene: (sceneId, editor) =>
        guarded([sceneId], async () => {
          const { head, tail } = splitBody(editor.state.doc, editor.state.selection.$from.index(0));
          const { version } = savedOf(sceneId);
          const result = await blockApi.splitScene(sceneId, {
            headBody: head,
            tailBody: tail,
            version,
          });
          addSceneProse(queryClient, bookId, {
            sceneId: result.newSceneId,
            body: tail,
            version: result.newSceneVersion,
          });
          setSceneProse(queryClient, bookId, { sceneId, body: head, version: result.version });
          await invalidateOutline(queryClient, bookId);
          await refreshBeats();
        }),
      moveBeat: (beatId, fromId, toId, index) => move(beatId, fromId, toId, index, false),
      deleteBeat: (beatId, sceneId, keepProse) =>
        guarded([sceneId], async () => {
          const before = savedOf(sceneId);
          const extracted = extractUnit(before.body, beatId);
          if (!extracted) return;
          const body = keepProse
            ? withUnit(extracted.rest, extracted.unit.slice(1), extracted.at, extracted.like)
            : bodyOf(extracted.rest, extracted.like);
          const saved = await manuscriptApi.saveProse(sceneId, { body, version: before.version });
          setSceneProse(queryClient, bookId, { sceneId, body, version: saved.version });
          await refreshBeats();
          // Putting the line back brings the archived beat back with it.
          showToast({
            message: keepProse ? 'Beat deleted · prose kept' : 'Beat and its prose deleted',
            undo: () =>
              guarded([sceneId], async () => {
                const restored = await manuscriptApi.saveProse(sceneId, {
                  body: before.body,
                  version: savedOf(sceneId).version,
                });
                setSceneProse(queryClient, bookId, {
                  sceneId,
                  body: before.body,
                  version: restored.version,
                });
                await refreshBeats();
              }),
          });
        }),
      stepBeat: async (beatId, sceneId, direction) => {
        if (!(await settled([sceneId]))) return;
        const step: Step = stepTarget(savedOf(sceneId).body, beatId, direction);
        if (step.scene === 'same') return move(beatId, sceneId, sceneId, step.index, true);
        const order = sceneOrder();
        const neighbour = order[order.indexOf(sceneId) + (step.scene === 'next' ? 1 : -1)];
        if (neighbour) return move(beatId, sceneId, neighbour, step.index, false);
      },
    };
  }, [bookId, queryClient, coordinator, registry]);
}
