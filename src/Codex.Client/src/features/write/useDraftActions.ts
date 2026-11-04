import { useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { ApiRequestError } from '@codex/core/api/client';
import { invalidateOutline } from '@codex/core/api/outline';
import { markersKey } from '@codex/core/api/references';
import {
  draftApi,
  draftEvents,
  removeDraft,
  sceneTarget,
  setDraft,
  setSceneProse,
  type Draft,
  type GenerateDraftRequest,
} from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { countWords } from '@codex/core/features/write/draftText';
import { draftStore } from './draftStore';
import { useSaveCoordinator } from './saveCoordinator';
import { useSceneRegistry } from './sceneRegistry';

/** Where a generation goes: a beat's draft, or the end of a scene (no beat). */
export interface DraftTarget {
  sceneId: string;
  beatId?: string;
}

export interface DraftActions {
  generate(target: DraftTarget, request: GenerateDraftRequest): Promise<void>;
  /** Stops the stream; the text received so far is kept as a pending draft. */
  stop(target: string): void;
  apply(draft: Draft): Promise<void>;
  discard(draft: Draft): Promise<void>;
  /** Drops a failed generation from the screen. */
  dismiss(target: string): void;
}

const messageOf = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

// Generate, stop, apply, discard and the two undos. A running generation lives in `draftStore`; the finished draft
// goes into the drafts cache, which is what the panel shows once nothing is live for its target.
export function useDraftActions(): DraftActions {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const registry = useSceneRegistry();
  const coordinator = useSaveCoordinator();

  return useMemo(() => {
    const guarded = async (
      sceneId: string,
      work: () => Promise<void>,
      fallback: string,
      offline?: string,
    ) => {
      try {
        await work();
      } catch (error) {
        if (error instanceof ApiRequestError && error.status === 409) {
          coordinator.store(sceneId).set('conflict');
          return;
        }
        // The request never reached the server: say so, rather than the browser's "Failed to fetch".
        if (offline !== undefined && !(error instanceof ApiRequestError)) {
          showToast({ message: offline });
          return;
        }
        showToast({ message: messageOf(error, fallback) });
      }
    };
    const writeScene = (sceneId: string, result: { body: string; version: number }) =>
      setSceneProse(queryClient, bookId, {
        sceneId,
        body: result.body,
        version: result.version,
      });
    // The minimap's "pending draft" tick comes from the outline; a draft's source markers come with the draft.
    const refreshOutline = () => {
      void queryClient.invalidateQueries({ queryKey: markersKey(bookId) });
      return invalidateOutline(queryClient, bookId);
    };
    const savedVersion = async (sceneId: string) => {
      const handle = registry.get(sceneId);
      if (!handle || !(await handle.settle())) return null;
      return handle.saved().version;
    };

    const generate: DraftActions['generate'] = async (where, request) => {
      const target = where.beatId ?? sceneTarget(where.sceneId);
      const base = {
        target,
        sceneId: where.sceneId,
        generationId: null,
        attempt: null,
        text: '',
        error: null,
        request,
        templateName: null,
      };
      draftStore.setLive({ ...base, status: 'starting' });
      const controller = draftStore.controller(target);

      // What the model reads is the saved scene, so unsaved typing goes first.
      if ((await savedVersion(where.sceneId)) === null) {
        draftStore.setLive({
          ...base,
          status: 'failed',
          error: 'The scene could not be saved, so no draft was started.',
        });
        return;
      }

      let generationId: string | null = null;
      let attempt = 1;
      let templateName: string | null = null;
      let model = request.model;
      let text = '';
      let ended = false;
      try {
        const chunks = where.beatId
          ? await draftApi.generate(where.beatId, request, controller.signal)
          : await draftApi.continueScene(where.sceneId, request, controller.signal);
        for await (const event of draftEvents(chunks)) {
          if (event.started) {
            generationId = event.started.generationId;
            attempt = event.started.attempt;
            templateName = event.started.template;
            model = event.started.model;
            if (request.retryOfId) removeDraft(queryClient, bookId, request.retryOfId);
            draftStore.patchLive(target, {
              status: 'streaming',
              generationId,
              attempt,
              templateName,
            });
          } else if (event.text !== undefined) {
            text += event.text;
            draftStore.patchLive(target, { text });
          } else if (event.final) {
            ended = true;
            setDraft(queryClient, bookId, {
              id: event.final.generationId,
              sceneId: where.sceneId,
              beatId: where.beatId ?? null,
              kind: where.beatId ? 'beat_prose' : 'continue',
              model: event.final.model,
              templateId: request.templateId,
              template: templateName,
              targetWords: request.targetWords,
              attempt,
              status: 'pending',
              text,
              words: event.final.words,
              retryOfId: request.retryOfId,
              inputTokens: event.final.inputTokens,
              outputTokens: event.final.outputTokens,
              integrationId: request.integrationId ?? null,
            });
            draftStore.clearLive(target);
            void refreshOutline();
          } else if (event.error) {
            ended = true;
            draftStore.patchLive(target, { status: 'failed', error: event.error.error });
          }
        }
      } catch (error) {
        if (!controller.signal.aborted) {
          ended = true;
          draftStore.setLive({
            ...base,
            status: 'failed',
            generationId,
            attempt,
            error: messageOf(error, 'The draft could not be generated.'),
          });
        }
      }
      if (ended) return;

      // Stop (or a connection that closed): the server keeps the text received so far as a pending draft.
      if (generationId !== null && text !== '') {
        setDraft(queryClient, bookId, {
          id: generationId,
          sceneId: where.sceneId,
          beatId: where.beatId ?? null,
          kind: where.beatId ? 'beat_prose' : 'continue',
          model,
          templateId: request.templateId,
          template: templateName,
          targetWords: request.targetWords,
          attempt,
          status: 'pending',
          text,
          words: countWords(text),
          retryOfId: request.retryOfId,
          inputTokens: null,
          outputTokens: null,
          stopped: controller.signal.aborted,
          integrationId: request.integrationId ?? null,
        });
      }
      draftStore.clearLive(target);
      void refreshOutline();
    };

    const apply: DraftActions['apply'] = (draft) =>
      guarded(
        draft.sceneId,
        async () => {
          const version = await savedVersion(draft.sceneId);
          if (version === null) return;
          // The apply writes the scene, so the book bar goes Saving… then Saved like any other save.
          const status = coordinator.store(draft.sceneId);
          status.set('saving');
          let result;
          try {
            result = await draftApi.apply(draft.id, version);
          } catch (error) {
            status.set('saved');
            throw error;
          }
          removeDraft(queryClient, bookId, draft.id);
          writeScene(draft.sceneId, result);
          status.set('saved');
          void refreshOutline();
          showToast({
            message: `Applied ${draft.words} words`,
            undo: () =>
              guarded(
                draft.sceneId,
                async () => {
                  const current = (await savedVersion(draft.sceneId)) ?? result.version;
                  const restored = await draftApi.undoApply(draft.id, current);
                  writeScene(draft.sceneId, restored);
                  setDraft(queryClient, bookId, draft);
                  void refreshOutline();
                },
                'The apply could not be undone.',
              ),
          });
        },
        'The draft could not be applied.',
        'Not applied. You are offline.',
      );

    const discard: DraftActions['discard'] = (draft) =>
      guarded(
        draft.sceneId,
        async () => {
          await draftApi.discard(draft.id);
          removeDraft(queryClient, bookId, draft.id);
          void refreshOutline();
          showToast({
            message: 'Draft discarded',
            undo: () =>
              guarded(
                draft.sceneId,
                async () => {
                  setDraft(queryClient, bookId, await draftApi.undoDiscard(draft.id));
                  void refreshOutline();
                },
                'The discard could not be undone.',
              ),
          });
        },
        'The draft could not be discarded.',
      );

    return {
      generate,
      stop: (target) => draftStore.stop(target),
      apply,
      discard,
      dismiss: (target) => draftStore.clearLive(target),
    };
  }, [bookId, queryClient, registry, coordinator]);
}
