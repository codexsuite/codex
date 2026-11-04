import { useEffect, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { manuscriptApi } from '@codex/core/api/manuscript';
import { ApiRequestError } from '@codex/core/api/client';
import { showToast } from '@codex/core/editing/toastStore';
import {
  keepProseDraft,
  readProseDraft,
  setSceneProse,
  storeUnsavedProse,
} from '@codex/core/api/write';
import type { ChapterLinkLabel } from '@codex/core/features/write/manuscriptView';
import { useSaveCoordinator } from './saveCoordinator';
import { SceneEditor } from './SceneEditor';
import { ServerCopy } from '@codex/core/features/write/serverCopy';

interface SceneProseProps {
  sceneId: string;
  body: string;
  version: number;
  chapterLinks: ReadonlyMap<string, ChapterLinkLabel>;
}

interface Loaded {
  body: string;
  version: number;
  /** Bumped when the server's copy replaces what is on screen (a reload); remounts the editor. */
  epoch: number;
}

// A scene's prose, editable, saved by `PUT /api/scenes/{id}/prose` with the version it was loaded at (a stale save
// still wins; see R6).
// The manuscript cache is the source of the first text and of reloads. A save writes its body and new version back
// to the cache (so a remount starts from what was saved), and that echo is recognised and ignored; anything else
// that arrives in the cache while nothing is waiting to be saved is the server's newer copy and replaces the text.
export function SceneProse({ sceneId, body, version, chapterLinks }: SceneProseProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const coordinator = useSaveCoordinator();
  const store = useMemo(() => coordinator.store(sceneId), [coordinator, sceneId]);
  useEffect(() => () => coordinator.release(sceneId), [coordinator, sceneId]);

  // What the server is known to hold for this scene (the loaded copy, then each save's). Mutated by `save` only.
  const [known] = useState(() => new ServerCopy(body, version));
  const [loaded, setLoaded] = useState<Loaded>({ body, version, epoch: 0 });
  if (!known.is(body, version) && store.getState() === 'saved') {
    known.set(body, version);
    setLoaded({ body, version, epoch: loaded.epoch + 1 });
  }

  const save = async (text: string, based: number) => {
    const sending = (async () => {
      let result: { version: number };
      try {
        result = await manuscriptApi.saveProse(sceneId, { body: text, version: based });
      } catch (error) {
        // Not saved and not refused as stale: keep the text so leaving the screen does not lose it.
        const stale = error instanceof ApiRequestError && error.status === 409;
        const kept = stale ? null : { body: text, version: based };
        keepProseDraft(queryClient, bookId, sceneId, kept);
        storeUnsavedProse(sceneId, kept);
        // The server refused an invented beat or reference id: say why, the status alone would not.
        if (error instanceof ApiRequestError && error.status === 400) {
          showToast({ message: error.message });
        }
        throw error;
      }
      keepProseDraft(queryClient, bookId, sceneId, null);
      storeUnsavedProse(sceneId, null);
      known.set(text, result.version);
      setSceneProse(queryClient, bookId, { sceneId, body: text, version: result.version });
      // Last write wins (R6): the server held a newer version than this edit was based on and has kept that text in
      // the scene's history. This text is now the stored one, so "Updated elsewhere" is answered.
      if (result.version !== based + 1) coordinator.clearRemoteChange();
      return result;
    })();
    known.track(sending);
    return sending;
  };

  // A body kept from a failed save comes back as an unsaved edit, which the next edit or blur retries.
  const [draft] = useState(() => readProseDraft(queryClient, bookId, sceneId, version)?.body);

  return (
    <SceneEditor
      key={loaded.epoch}
      sceneId={sceneId}
      copy={known}
      draft={loaded.epoch === 0 ? draft : undefined}
      initial={loaded.body}
      version={loaded.version}
      save={save}
      store={store}
      chapterLinks={chapterLinks}
    />
  );
}
