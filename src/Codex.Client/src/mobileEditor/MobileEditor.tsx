import { useCallback, useEffect, useMemo, useReducer, useRef } from 'react';
import type { Editor } from '@tiptap/react';
import {
  EDITOR_BRIDGE_VERSION,
  type EditorCommand,
  type HostMessage,
} from '@codex/core/editing/editorBridge';
import { beatAction, newSceneAction } from './blockActions';
import { ChangeSender } from './ChangeSender';
import { StateReporter } from './editorState';
import { syncSelection } from './syncSelection';
import { initialPageState, pageReducer } from './pageState';
import { runCommand, type BlockActions } from './runCommand';
import { SceneSection } from './SceneSection';
import { mobileEditorTestIds } from './testIds';
import { onHostMessage, sendToHost } from './transport';

/** How long typing must pause before the app is told (the app autosaves what it is told). */
const CHANGE_DELAY_MS = 400;

// The page the app loads into its WebView (#35): the chapter's scenes, each a card and an editor, driven by the bridge
// (`@codex/core/editing/editorBridge`). The page keeps no state of its own about saving: it reports text and selection,
// the app saves, and `setScene` brings back anything that did not come from this page's typing.
export function MobileEditor() {
  const [state, dispatch] = useReducer(pageReducer, initialPageState);
  const stateNow = useRef(state);
  const editors = useRef(new Map<string, Editor>());
  const elements = useRef(new Map<string, HTMLElement>());
  const root = useRef<HTMLDivElement>(null);
  // The scene whose editor last had focus: the accessory bar's commands go there, wherever `scrollTo` has moved the view.
  const focusedId = useRef<string | null>(null);
  // Scenes whose split the app is carrying out: their waiting change is stale once the app rewrites the scene.
  const splitting = useRef(new Set<string>());
  // Scenes with any part in view, for the card that is open while reading.
  const visible = useRef(new Set<string>());
  // A scene the app scrolled to (`scrollTo`, `load`) keeps the open card until the reader scrolls: the end of the
  // chapter cannot scroll its last, short scene to the top.
  const pinned = useRef<string | null>(null);

  const sender = useMemo(
    () =>
      new ChangeSender(
        (sceneId, markdown) => sendToHost({ type: 'change', sceneId, markdown }),
        CHANGE_DELAY_MS,
      ),
    [],
  );
  const reporter = useMemo(() => new StateReporter(sendToHost), []);

  const actionsFor = useCallback(
    (sceneId: string): BlockActions => {
      // The app saves the scene before it acts, so what was typed must reach it first.
      const ask = (build: typeof beatAction, splits: boolean) => (editor: Editor) => {
        if (stateNow.current.readOnly) return;
        sender.flush(sceneId);
        if (splits) splitting.current.add(sceneId);
        sendToHost({ type: 'action', sceneId, ...build(editor) });
      };
      return { beat: ask(beatAction, false), newScene: ask(newSceneAction, true) };
    },
    [sender],
  );

  const handleCommand = useCallback(
    (command: EditorCommand) => {
      const { activeSceneId, readOnly } = stateNow.current;
      const sceneId = focusedId.current ?? activeSceneId;
      const editor = sceneId === null ? undefined : editors.current.get(sceneId);
      if (!editor || sceneId === null) return;
      if (readOnly && command !== 'blur') return;
      syncSelection(editor);
      runCommand(editor, command, actionsFor(sceneId));
      reporter.report(editor);
    },
    [actionsFor, reporter],
  );

  const handleHost = useCallback(
    (message: HostMessage) => {
      switch (message.type) {
        case 'load':
          document.documentElement.dataset.theme = message.theme;
          document.documentElement.dataset.density = message.density;
          // Typing not yet sent still belongs to the chapter being left; only a scene the app is splitting has stale text.
          sender.flushAllExcept(splitting.current);
          splitting.current.clear();
          focusedId.current = null;
          pinned.current = message.scrollToSceneId;
          visible.current.clear();
          reporter.reset();
          dispatch({ ...message, type: 'load' });
          return;
        case 'setScene':
        case 'sceneMeta':
          dispatch(message);
          return;
        case 'command':
          handleCommand(message.command);
          return;
        case 'scrollTo':
          pinned.current = message.sceneId;
          dispatch({ type: 'reading', sceneId: message.sceneId });
          elements.current.get(message.sceneId)?.scrollIntoView({ block: 'start' });
          return;
      }
    },
    [handleCommand, reporter, sender],
  );

  useEffect(() => {
    stateNow.current = state;
  });

  useEffect(() => {
    const stop = onHostMessage(handleHost);
    // Subscribed before announcing: the app answers `ready` with `load`.
    sendToHost({ type: 'ready', v: EDITOR_BRIDGE_VERSION });
    return stop;
  }, [handleHost]);

  // A chapter the app asked to open at a scene.
  useEffect(() => {
    const target = state.scrollToSceneId;
    if (target !== null) elements.current.get(target)?.scrollIntoView({ block: 'start' });
  }, [state.epoch, state.scrollToSceneId]);

  // Typed text must not be lost when the app goes to the background.
  useEffect(() => {
    const flush = () => {
      if (document.visibilityState === 'hidden') sender.flushAll();
    };
    const flushNow = () => sender.flushAll();
    document.addEventListener('visibilitychange', flush);
    window.addEventListener('pagehide', flushNow);
    return () => {
      document.removeEventListener('visibilitychange', flush);
      window.removeEventListener('pagehide', flushNow);
    };
  }, [sender]);

  useEffect(() => {
    const report = (event: ErrorEvent) => sendToHost({ type: 'error', message: event.message });
    window.addEventListener('error', report);
    return () => window.removeEventListener('error', report);
  }, []);

  // The WebView may need the height to size itself.
  useEffect(() => {
    const element = root.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    let last = -1;
    const observer = new ResizeObserver(() => {
      // Nothing to size before the first `load`.
      if (!stateNow.current.loaded) return;
      const height = Math.ceil(element.getBoundingClientRect().height);
      if (height === last) return;
      last = height;
      sendToHost({ type: 'contentHeight', height });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const topmostVisible = useCallback((): string | null => {
    if (pinned.current !== null) return pinned.current;
    const first = stateNow.current.scenes.find((scene) => visible.current.has(scene.id));
    return first?.id ?? null;
  }, []);

  // While reading, the card of the topmost scene in view is open. The root is shrunk by a pixel so a scene that only
  // touches the edge of the screen does not count.
  useEffect(() => {
    if (typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          const id = (entry.target as HTMLElement).dataset.sceneId;
          if (id === undefined) continue;
          if (entry.isIntersecting) visible.current.add(id);
          else visible.current.delete(id);
        }
        const top = topmostVisible();
        if (top !== null) dispatch({ type: 'reading', sceneId: top });
      },
      { rootMargin: '-1px 0px -1px 0px' },
    );
    for (const element of elements.current.values()) observer.observe(element);
    return () => observer.disconnect();
  }, [state.epoch, topmostVisible]);

  // The reader scrolling (wheel, touch, keys) ends a pin.
  useEffect(() => {
    const unpin = () => {
      pinned.current = null;
    };
    const events = ['wheel', 'touchmove', 'keydown'] as const;
    for (const name of events) window.addEventListener(name, unpin, { passive: true });
    return () => {
      for (const name of events) window.removeEventListener(name, unpin);
    };
  }, []);

  const onEditor = useCallback((sceneId: string, editor: Editor | null) => {
    if (editor) editors.current.set(sceneId, editor);
    else editors.current.delete(sceneId);
  }, []);
  const elementRef = useCallback((sceneId: string, element: HTMLElement | null) => {
    if (element) elements.current.set(sceneId, element);
    else elements.current.delete(sceneId);
  }, []);
  const onFocus = useCallback(
    (sceneId: string, editor: Editor) => {
      focusedId.current = sceneId;
      dispatch({ type: 'editing', editing: true });
      sendToHost({ type: 'focus', sceneId });
      reporter.report(editor);
    },
    [reporter],
  );
  const onBlur = useCallback(
    (sceneId: string) => {
      sender.flush(sceneId);
      sendToHost({ type: 'blur', sceneId });
      dispatch({ type: 'editing', editing: false });
      const top = topmostVisible();
      if (top !== null) dispatch({ type: 'reading', sceneId: top });
    },
    [sender, topmostVisible],
  );
  const onTransaction = useCallback(
    (editor: Editor) => {
      if (editor.isFocused) reporter.report(editor);
    },
    [reporter],
  );
  const onChange = useCallback(
    (sceneId: string, markdown: string) => sender.schedule(sceneId, markdown),
    [sender],
  );
  const onOpenScene = useCallback(
    (sceneId: string) => sendToHost({ type: 'openScene', sceneId }),
    [],
  );
  const onTextReplaced = useCallback((sceneId: string) => sender.cancel(sceneId), [sender]);

  return (
    <div ref={root} className="me-root" data-testid={mobileEditorTestIds.root}>
      {state.scenes.map((scene) => (
        <SceneSection
          key={`${state.epoch}:${scene.id}`}
          scene={scene}
          replaced={state.replaced[scene.id] ?? 0}
          active={scene.id === state.activeSceneId}
          readOnly={state.readOnly}
          entries={state.entries}
          actionsFor={actionsFor}
          placeholder="Start writing…"
          onOpenScene={onOpenScene}
          onChange={onChange}
          onFocus={onFocus}
          onBlur={onBlur}
          onTextReplaced={onTextReplaced}
          onEditor={onEditor}
          onTransaction={onTransaction}
          elementRef={elementRef}
        />
      ))}
    </div>
  );
}
