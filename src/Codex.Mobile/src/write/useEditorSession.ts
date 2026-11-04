import { ApiRequestError } from '@codex/core/api/client';
import { kbKey, type Kb } from '@codex/core/api/kb';
import { manuscriptApi } from '@codex/core/api/manuscript';
import { outlineKey } from '@codex/core/api/outline';
import type { Outline } from '@codex/core/api/structure';
import {
  additionsKey,
  beatsKey,
  keepProseDraft,
  manuscriptKey,
  readProseDraft,
  setSceneProse,
  storeUnsavedProse,
  type BookBeats,
  type BookKbAdditions,
  type Manuscript,
} from '@codex/core/api/write';
import {
  decodePageMessage,
  EDITOR_BRIDGE_VERSION,
  encodeHostMessage,
  type EditorCommand,
  type HostMessage,
  type PageMessage,
} from '@codex/core/editing/editorBridge';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState, type ComponentRef, type RefObject } from 'react';
import { AppState } from 'react-native';
import type { WebView, WebViewMessageEvent } from 'react-native-webview';
import { bookSaveStatus } from '~/book/saveStatus';
import { useTheme } from '~/theme';
import { insertBeat, splitScene, type BlockOutcome } from './blockActions';
import { buildLoad, buildScene, sceneMetaOf, type WriteData } from './buildLoad';
import { chaptersOf, type WriteChapter } from './chapters';
import { isEditorPage } from './pageGuards';
import { SceneSaves } from './sceneSaves';

type WebViewRef = ComponentRef<typeof WebView>;

/** What the toolbar above the keyboard reflects about the editor. */
export interface EditorFormat {
  bold: boolean;
  italic: boolean;
  quote: boolean;
  canUndo: boolean;
  canRedo: boolean;
}

const noFormat: EditorFormat = {
  bold: false,
  italic: false,
  quote: false,
  canUndo: false,
  canRedo: false,
};

export interface EditorSessionInput {
  bookId: string;
  chapter: WriteChapter;
  scrollToSceneId: string | null;
  /** Changes every time Write is navigated to (also with the same params), so a repeated link scrolls again. */
  linkNonce: number;
  readOnly: boolean;
  onOpenScene(sceneId: string): void;
}

export interface EditorSession {
  webViewRef: RefObject<WebViewRef | null>;
  onMessage(event: WebViewMessageEvent): void;
  /** The page could not be shown (WebView error, a version the app does not speak). */
  failure: string | null;
  /** A problem worth telling the writer that does not stop the page. Clears on the next saved edit or a dismiss. */
  notice: string | null;
  dismissNotice(): void;
  focusedSceneId: string | null;
  format: EditorFormat;
  command(command: EditorCommand): void;
  /** Loads the page again (Retry after a failure). */
  reloadPage(): void;
  /** The WebView could not load the page. */
  pageFailed(message: string): void;
  /** The WebView finished loading the page. */
  pageLoaded(): void;
  /** The WebView's web process died: save, then load the page again. */
  pageProcessGone(): void;
  /** Bumps when the page must be remounted (the WebView `key`). */
  generation: number;
  /** Saves what is pending (leaving the screen). */
  flush(): Promise<boolean>;
}

/** How long the page has to say `ready` once the WebView has finished loading it. */
export const EDITOR_READY_TIMEOUT_MS = 10_000;
/** The overall cap from mounting the WebView (a page that never finishes loading on a bad connection). */
export const EDITOR_MOUNT_TIMEOUT_MS = 30_000;
export const EDITOR_START_TIMEOUT_MESSAGE =
  'The editor did not start. Check your connection and that the server is running, then try again.';

type Cached = { [K in keyof WriteData]: WriteData[K] | undefined };

const messageOf = (error: unknown): string =>
  error instanceof Error ? error.message : 'The change could not be saved.';

/**
 * The app's side of the editor bridge for the open chapter: the handshake, `load`, `scrollTo` and `sceneMeta`, one autosave
 * per scene, and the block operations the page asks for. Data is read from the query cache when it is sent, never on a
 * render, so a background refetch cannot replace what is being typed.
 */
export function useEditorSession({
  bookId,
  chapter,
  scrollToSceneId,
  linkNonce,
  readOnly,
  onOpenScene,
}: EditorSessionInput): EditorSession {
  const queryClient = useQueryClient();
  const theme = useTheme();
  const webViewRef = useRef<WebViewRef>(null);
  const [generation, setGeneration] = useState(0);
  const [ready, setReady] = useState(0);
  // The WebView has finished loading the page (it may not have started the editor yet).
  const [loaded, setLoaded] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [focusedSceneId, setFocusedSceneId] = useState<string | null>(null);
  const [format, setFormat] = useState<EditorFormat>(noFormat);
  // What the page shows now: `${chapterId}|${readOnly}`, its scene ids and theme, and the card details last sent per scene.
  const loadedKey = useRef<string | null>(null);
  const loadedIds = useRef<string[]>([]);
  const loadedScheme = useRef(theme.scheme);
  const sentMeta = useRef(new Map<string, string>());
  const requestSeq = useRef(0);
  // One block operation at a time; page `change`s that arrive meanwhile are held (a late autosave of the old text would
  // drop the directive the operation is inserting).
  const blockOp = useRef<string | null>(null);
  const held = useRef(new Map<string, string>());
  const latest = useRef({ chapter, scrollToSceneId, readOnly, scheme: theme.scheme, onOpenScene });
  latest.current = { chapter, scrollToSceneId, readOnly, scheme: theme.scheme, onOpenScene };

  const send = useCallback((message: HostMessage) => {
    webViewRef.current?.postMessage(encodeHostMessage(message));
  }, []);

  const complete = useCallback((): WriteData | null => {
    const data: Cached = {
      outline: queryClient.getQueryData<Outline>(outlineKey(bookId)),
      manuscript: queryClient.getQueryData<Manuscript>(manuscriptKey(bookId)),
      kb: queryClient.getQueryData<Kb>(kbKey(bookId)),
      beats: queryClient.getQueryData<BookBeats>(beatsKey(bookId)),
      additions: queryClient.getQueryData<BookKbAdditions>(additionsKey(bookId)),
    };
    return data.outline && data.manuscript && data.kb && data.beats && data.additions
      ? {
          outline: data.outline,
          manuscript: data.manuscript,
          kb: data.kb,
          beats: data.beats,
          additions: data.additions,
        }
      : null;
  }, [queryClient, bookId]);

  const showServerCopyRef = useRef<(sceneId: string) => void>(() => {});
  const reloadFromServerRef = useRef<(sceneId: string) => void>(() => {});
  const savesRef = useRef<SceneSaves | null>(null);
  if (savesRef.current === null) {
    savesRef.current = new SceneSaves({
      bookId,
      sources: bookSaveStatus.forBook(bookId),
      saveProse: async (sceneId, body, version) => {
        try {
          return await manuscriptApi.saveProse(sceneId, { body, version });
        } catch (error) {
          // A refused save (400: the text is not allowed; 403: the role dropped) says why; there is no toast host.
          if (error instanceof ApiRequestError && (error.status === 400 || error.status === 403)) {
            setNotice(error.message);
          }
          throw error;
        }
      },
      onSaved: (sceneId, body, version) => {
        setSceneProse(queryClient, bookId, { sceneId, body, version });
        setNotice(null);
      },
      onConflict: (sceneId) => reloadFromServerRef.current(sceneId),
      onForbidden: (sceneId) => showServerCopyRef.current(sceneId),
      keepDraft: (sceneId, draft) => {
        keepProseDraft(queryClient, bookId, sceneId, draft);
        storeUnsavedProse(sceneId, draft);
      },
      readDraft: (sceneId, serverVersion) =>
        readProseDraft(queryClient, bookId, sceneId, serverVersion),
    });
  }
  const saves = savesRef.current;

  // A scene the server holds a different copy of than the page does: the page gets the server's.
  const showServerCopy = useCallback(
    (sceneId: string) => {
      const data = complete();
      const scene =
        data &&
        chaptersOf(data.outline)
          .flatMap((c) => c.scenes)
          .find((s) => s.id === sceneId);
      if (!data || !scene) return;
      const built = buildScene(scene, data);
      saves.reset(sceneId, built.prose, built.version);
      sentMeta.current.set(sceneId, JSON.stringify(sceneMetaOf(built)));
      send({ type: 'setScene', scene: built });
    },
    [complete, saves, send],
  );
  showServerCopyRef.current = showServerCopy;

  // The server refused a version as stale: read the scene again, and the page shows the server's copy.
  const reloadFromServer = useCallback(
    (sceneId: string) => {
      void manuscriptApi
        .prose(sceneId)
        .then((prose) => {
          setSceneProse(queryClient, bookId, {
            sceneId,
            body: prose.body,
            version: prose.version,
          });
          showServerCopy(sceneId);
        })
        .catch((error: unknown) => setNotice(messageOf(error)));
    },
    [queryClient, bookId, showServerCopy],
  );
  reloadFromServerRef.current = reloadFromServer;

  // Leaving the screen saves what it can; text that could not be saved is kept as a draft for the next visit.
  useEffect(
    () => () => {
      void saves.settle().finally(() => saves.dispose());
    },
    [saves],
  );

  // Backgrounding the app may be the last the app runs: save now.
  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state !== 'active') void saves.settle();
    });
    return () => subscription.remove();
  }, [saves]);

  const loadChapter = useCallback(
    (scrollOverride?: string) => {
      const { chapter: target, scrollToSceneId: scrollTo, readOnly: ro, scheme } = latest.current;
      const data = complete();
      if (!data) return;
      // The outline in the cache is the truth for which scenes the chapter holds right now.
      const fresh = chaptersOf(data.outline).find((c) => c.id === target.id) ?? target;
      const built = buildLoad(fresh, data, {
        theme: scheme,
        density: 'touch',
        readOnly: ro,
        scrollToSceneId: scrollOverride ?? scrollTo,
      });
      // A scene with unsaved text keeps it: the page is given that text, not the server's older copy.
      const message = {
        ...built,
        scenes: built.scenes.map((scene) => {
          const shown = saves.open(scene.id, { body: scene.prose, version: scene.version });
          return { ...scene, prose: shown.body, version: shown.version };
        }),
      };
      saves.retain(message.scenes.map((s) => s.id));
      sentMeta.current = new Map(message.scenes.map((s) => [s.id, JSON.stringify(sceneMetaOf(s))]));
      loadedKey.current = `${target.id}|${ro}`;
      loadedIds.current = message.scenes.map((s) => s.id);
      loadedScheme.current = scheme;
      setFocusedSceneId(null);
      setFormat(noFormat);
      send(message);
    },
    [complete, saves, send],
  );

  // Settle the open chapter's saves, then load it again, unless a newer request came in meanwhile.
  const reloadAfterSettling = useCallback(() => {
    const seq = ++requestSeq.current;
    void saves.settle().then(() => {
      if (seq === requestSeq.current) loadChapter();
    });
  }, [saves, loadChapter]);

  // The page is ready or the place changed: scroll when only the scene changed, load when the chapter did.
  useEffect(() => {
    if (ready === 0 || failure) return;
    if (loadedKey.current === `${chapter.id}|${readOnly}`) {
      if (scrollToSceneId) send({ type: 'scrollTo', sceneId: scrollToSceneId });
      return;
    }
    reloadAfterSettling();
    // linkNonce: a link to the place already open still scrolls to it.
  }, [ready, failure, chapter.id, scrollToSceneId, linkNonce, readOnly, send, reloadAfterSettling]);

  // A theme change reloads the page, but not under the writer's cursor: it waits for the editor to lose focus.
  useEffect(() => {
    if (ready === 0 || failure || loadedKey.current === null) return;
    if (loadedScheme.current === theme.scheme || focusedSceneId !== null) return;
    reloadAfterSettling();
  }, [ready, failure, theme.scheme, focusedSceneId, reloadAfterSettling]);

  // The scene sheet (or Plan, another session) changed the chapter's cards: new details go to the page without touching
  // the prose or the cursor; a changed list of scenes (added, archived, moved) is a new load.
  useEffect(() => {
    if (ready === 0 || failure || loadedKey.current === null || blockOp.current !== null) return;
    const data = complete();
    const fresh = data && chaptersOf(data.outline).find((c) => c.id === chapter.id);
    if (!data || !fresh || loadedKey.current !== `${fresh.id}|${readOnly}`) return;
    if (fresh.scenes.map((s) => s.id).join() !== loadedIds.current.join()) {
      reloadAfterSettling();
      return;
    }
    for (const scene of fresh.scenes) {
      const meta = sceneMetaOf(buildScene(scene, data));
      const signature = JSON.stringify(meta);
      if (sentMeta.current.get(scene.id) === signature) continue;
      sentMeta.current.set(scene.id, signature);
      send(meta);
    }
  }, [chapter, ready, failure, readOnly, complete, send, reloadAfterSettling]);

  const finishBlock = useCallback(
    (outcome: BlockOutcome, sceneId: string) => {
      // Text the page reported during the operation: for the scene it changed, the server's copy replaces it (unless the
      // operation failed without the server rejecting the scene's version); for the others it is the newest text.
      const heldNow = [...held.current];
      held.current.clear();
      blockOp.current = null;
      const replaced = outcome.status === 'done' || outcome.stale;
      for (const [id, markdown] of heldNow) {
        if (!(id === sceneId && replaced)) saves.change(id, markdown);
      }
      if (outcome.status === 'failed') {
        setNotice(outcome.message);
        if (outcome.stale) reloadFromServer(sceneId);
        return;
      }
      if (outcome.newSceneId) {
        // The chapter has a new scene: a fresh `load`, scrolled to it.
        loadChapter(outcome.newSceneId);
      } else {
        showServerCopy(sceneId);
      }
    },
    [saves, loadChapter, showServerCopy, reloadFromServer],
  );

  const runBlock = useCallback(
    (sceneId: string, run: () => Promise<BlockOutcome>) => {
      if (blockOp.current !== null) {
        setNotice('Finish the current change first.');
        return;
      }
      blockOp.current = sceneId;
      held.current.clear();
      void run().then((outcome) => finishBlock(outcome, sceneId));
    },
    [finishBlock],
  );

  const handle = useCallback(
    (message: PageMessage) => {
      switch (message.type) {
        case 'ready':
          if (message.v !== EDITOR_BRIDGE_VERSION) {
            setFailure(
              `The editor page speaks version ${message.v} but this app speaks version ${EDITOR_BRIDGE_VERSION}. Update the app or the server.`,
            );
            return;
          }
          loadedKey.current = null;
          setFailure(null);
          setReady((count) => count + 1);
          return;
        case 'change':
          if (latest.current.readOnly) return;
          if (blockOp.current !== null) held.current.set(message.sceneId, message.markdown);
          else saves.change(message.sceneId, message.markdown);
          return;
        case 'focus':
          setFocusedSceneId(message.sceneId);
          return;
        case 'blur':
          setFocusedSceneId(null);
          void saves.settle();
          return;
        case 'openScene':
          latest.current.onOpenScene(message.sceneId);
          return;
        case 'state':
          setFormat({
            bold: message.bold,
            italic: message.italic,
            quote: message.quote,
            canUndo: message.canUndo,
            canRedo: message.canRedo,
          });
          return;
        case 'action': {
          const { sceneId } = message;
          if (latest.current.readOnly) return;
          const sceneIds = loadedIds.current;
          const deps = { queryClient, bookId, saves, sceneIds };
          runBlock(sceneId, () =>
            message.action === 'beat'
              ? insertBeat(deps, sceneId, message.beatId, message.body)
              : splitScene(deps, sceneId, message.headBody, message.tailBody),
          );
          return;
        }
        case 'contentHeight':
          return;
        case 'error':
          console.warn(`[editor page] ${message.message}`);
          if (loadedKey.current === null) setFailure(message.message);
          else setNotice(message.message);
          return;
      }
    },
    [saves, queryClient, bookId, runBlock],
  );

  const onMessage = useCallback(
    (event: WebViewMessageEvent) => {
      // Only the editor page may talk to the app (a link followed inside the WebView must not).
      if (!isEditorPage(event.nativeEvent.url)) {
        console.warn(`[editor page] ignored a message from ${event.nativeEvent.url}`);
        return;
      }
      const decoded = decodePageMessage(event.nativeEvent.data);
      if (!decoded.ok) {
        console.warn(`[editor page] ignored a message: ${decoded.reason}`);
        return;
      }
      handle(decoded.message);
    },
    [handle],
  );

  const command = useCallback(
    (name: EditorCommand) => send({ type: 'command', command: name }),
    [send],
  );

  // A page that never says `ready` (blocked, a script error, a server without the page) would show a blank screen. The
  // 10 s start when the WebView has loaded the page (a slow connection is not a broken page); 30 s from mount is the cap.
  useEffect(() => {
    if (ready > 0 || failure) return;
    const timer = setTimeout(
      () => setFailure(EDITOR_START_TIMEOUT_MESSAGE),
      EDITOR_MOUNT_TIMEOUT_MS,
    );
    return () => clearTimeout(timer);
  }, [ready, failure, generation]);
  useEffect(() => {
    if (!loaded || ready > 0 || failure) return;
    const timer = setTimeout(
      () => setFailure(EDITOR_START_TIMEOUT_MESSAGE),
      EDITOR_READY_TIMEOUT_MS,
    );
    return () => clearTimeout(timer);
  }, [loaded, ready, failure, generation]);

  const reloadPage = useCallback(() => {
    setFailure(null);
    setNotice(null);
    setReady(0);
    setLoaded(false);
    loadedKey.current = null;
    setGeneration((count) => count + 1);
  }, []);

  const pageProcessGone = useCallback(() => {
    void saves.settle().then(reloadPage);
  }, [saves, reloadPage]);

  const pageLoaded = useCallback(() => setLoaded(true), []);
  const pageFailed = useCallback((message: string) => setFailure(message), []);
  const dismissNotice = useCallback(() => setNotice(null), []);
  const flush = useCallback(() => saves.settle(), [saves]);

  return {
    webViewRef,
    onMessage,
    failure,
    notice,
    dismissNotice,
    focusedSceneId,
    format,
    command,
    reloadPage,
    pageLoaded,
    pageFailed,
    pageProcessGone,
    generation,
    flush,
  };
}
