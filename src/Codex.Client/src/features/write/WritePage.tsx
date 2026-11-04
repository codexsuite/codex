import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { manuscriptApi } from '@codex/core/api/manuscript';
import { useOutline } from '../../api/outlineQueries';
import { manuscriptKey, setSceneProse, writeApi, type Manuscript } from '@codex/core/api/write';
import { useManuscript } from '../../api/writeQueries';
import { SaveStatus } from '../../editing/SaveStatus';
import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { showToast } from '@codex/core/editing/toastStore';
import { useCanWrite } from '../../shell/bookAccess';
import { ErrorState } from '../../shell/ErrorState';
import { SlotFill } from '../../shell/Slots';
import { useDeepLink } from '../../shell/useDeepLink';
import { ActMarker } from './ActMarker';
import { HighlightChip } from './HighlightChip';
import { useUsedScenes } from './useUsedScenes';
import { useWriteHighlight } from './useWriteHighlight';
import { ChapterJump } from './ChapterJump';
import { ChapterSection } from './ChapterSection';
import { FindInManuscript } from './FindInManuscript';
import { FocusButton } from './FocusButton';
import { useFormat } from './formatStore';
import { MarkerCard } from './MarkerCard';
import { MarkerCardContext } from './markerCardContext';
import { useMarkers } from './useMarkers';
import type { Marker } from '@codex/core/api/references';
import { FormatMenu } from './FormatMenu';
import { previewAnchor } from './formatMenuPreview';
import { PreviewPane } from './PreviewPane';
import { previewTarget } from './previewScope';
import { usePreviewPane } from './previewStore';
import {
  buildManuscriptView,
  chapterLinkLabels,
  resolveTarget,
  type ActView,
} from '@codex/core/features/write/manuscriptView';
import { Minimap } from './Minimap';
import { PreparedParaphrase } from './PreparedParaphrase';
import { ReturnedCitation } from './ReturnedCitation';
import { SaveCoordinator, SaveCoordinatorContext } from './saveCoordinator';
import { SceneRegistry, SceneRegistryContext } from './sceneRegistry';
import { ScopeSelector } from './ScopeSelector';
import { scopeActs, wholeBook, type Scope } from '@codex/core/features/write/scope';
import { scrollContainerTo, useScrollToTarget } from './scrollToTarget';
import { writeTestIds } from './testIds';
import { useLeaveGuard } from './useLeaveGuard';
import { useReadingPosition } from './useReadingPosition';
import './write.css';

// Write: the whole manuscript as one continuous read, chapter after chapter, with act markers, a gutter beside
// each scene, a minimap and a chapter jump. `#scene=5.2` / `#chapter=5` scroll to and flash the target.
export function WritePage() {
  const { bookId = '' } = useParams();
  const deepLink = useDeepLink();
  const outline = useOutline(bookId);
  const manuscript = useManuscript(bookId);
  const scroller = useRef<HTMLDivElement>(null);
  const [scope, setScope] = useState<Scope>(wholeBook);
  const [format, changeFormat] = useFormat(bookId);
  const [previewOpen, setPreviewOpen] = usePreviewPane();
  const [focused, setFocused] = useState(false);
  const [highlighted] = useWriteHighlight(bookId);
  const usedScenes = useUsedScenes(highlighted);
  const queryClient = useQueryClient();
  const [coordinator] = useState(() => new SaveCoordinator(saveStatusStore));
  useEffect(() => () => coordinator.detach(), [coordinator]);
  const [registry] = useState(() => new SceneRegistry());
  const canWrite = useCanWrite();
  useLeaveGuard(coordinator);

  // Retry on the bar: save again each scene whose last save the server refused.
  const retry = () => {
    for (const sceneId of coordinator.refused()) void registry.get(sceneId)?.settle();
  };

  // Reload on the bar's "Conflict" or "Updated elsewhere": fetch each refused scene's current prose, and every scene
  // the server now holds a newer version of, and put it in the cache. The scene's editor adopts it (dropping the local
  // edits) once its state is back to saved.
  const reload = async () => {
    const refused = coordinator.conflicts();
    if (coordinator.remoteChanged()) {
      try {
        const fresh = await writeApi.manuscript(bookId);
        const cached = queryClient.getQueryData<Manuscript>(manuscriptKey(bookId));
        const known = new Map(cached?.scenes.map((s) => [s.sceneId, s.version]));
        for (const scene of fresh.scenes) {
          const was = known.get(scene.sceneId);
          if (was === undefined || was >= scene.version || refused.includes(scene.sceneId))
            continue;
          coordinator.store(scene.sceneId).set('saved');
          setSceneProse(queryClient, bookId, scene);
        }
        coordinator.clearRemoteChange();
      } catch (error) {
        showToast({
          message: error instanceof Error ? error.message : 'The manuscript could not be reloaded.',
        });
      }
    }
    for (const sceneId of refused) {
      try {
        const prose = await manuscriptApi.prose(sceneId);
        coordinator.store(sceneId).set('saved');
        setSceneProse(queryClient, bookId, {
          sceneId,
          body: prose.body,
          version: prose.version,
        });
      } catch (error) {
        showToast({
          message: error instanceof Error ? error.message : 'The scene could not be reloaded.',
        });
      }
    }
  };

  // Focus is a body class so the shell's bar and sidebar can fade without a shell change. Entering it moves focus
  // to the manuscript (the Focus button sits in the bar, and a focused bar does not fade); Esc leaves it and returns
  // focus to the manuscript.
  useEffect(() => {
    if (!focused) return;
    document.body.classList.add('write-focus');
    scroller.current?.focus({ preventScroll: true });
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setFocused(false);
      scroller.current?.focus({ preventScroll: true });
    };
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.classList.remove('write-focus');
    };
  }, [focused]);

  const acts = useMemo(
    () =>
      outline.data && manuscript.data ? buildManuscriptView(outline.data, manuscript.data) : [],
    [outline.data, manuscript.data],
  );
  const headings = useMemo(
    () =>
      new Map(
        acts.flatMap((a) =>
          a.chapters.flatMap((c) => c.scenes.map((s) => [s.scene.id, s.heading])),
        ),
      ),
    [acts],
  );
  const chapterLinks = useMemo(
    () => (outline.data ? chapterLinkLabels(outline.data) : new Map()),
    [outline.data],
  );
  const target = useMemo(
    () => (deepLink.ok ? resolveTarget(acts, deepLink.link) : null),
    [acts, deepLink],
  );
  useScrollToTarget(scroller, target);

  // The marker card: one for the page. `&ref=<source>` opens the card of that source's first marker once drawn.
  const [card, setCard] = useState<{ marker: Marker; anchor: HTMLElement } | null>(null);
  const openCard = useCallback((marker: Marker, anchor: HTMLElement) => {
    setCard((open) => (open?.marker.useId === marker.useId ? null : { marker, anchor }));
  }, []);
  const closeCard = useCallback(() => setCard(null), []);
  const { markers } = useMarkers(bookId);
  const wantedSource = deepLink.ok ? deepLink.link.ref : undefined;
  const wantedUse = markers?.find((m) => m.sourceId === wantedSource);
  const opened = useRef<string | null>(null);
  // The scroller only exists once the outline and the manuscript are both loaded.
  const ready = manuscript.data !== undefined && outline.data !== undefined;
  useEffect(() => {
    const root = scroller.current;
    if (!wantedUse || !root || !ready || opened.current === wantedUse.useId) return;
    const find = () =>
      root.querySelector<HTMLElement>(`[data-marker-use="${CSS.escape(wantedUse.useId)}"]`);
    const show = (element: HTMLElement) => {
      opened.current = wantedUse.useId;
      element.scrollIntoView({ block: 'center' });
      setCard({ marker: wantedUse, anchor: element });
    };
    const found = find();
    if (found) {
      show(found);
      return;
    }
    const observer = new MutationObserver(() => {
      const element = find();
      if (!element) return;
      observer.disconnect();
      show(element);
    });
    observer.observe(root, { subtree: true, childList: true });
    return () => observer.disconnect();
  }, [wantedUse, ready]);

  // Look again for the scene being read whenever what is drawn changes.
  const drawn = useMemo(() => ({ scope, acts }), [scope, acts]);
  const reading = useReadingPosition(scroller, drawn);

  const failed = outline.error ?? manuscript.error;
  if (failed) {
    return (
      <ErrorState
        testId={writeTestIds.loadError}
        title="The manuscript could not be loaded"
        detail={failed.message}
      />
    );
  }
  if (!outline.data || !manuscript.data) return null;

  const visible = scopeActs(acts, scope);
  const chapters = visible.flatMap((act) => act.chapters);
  const position = locate(visible, reading);
  const jump = (chapterId: string) => {
    const header = scroller.current?.querySelector<HTMLElement>(`[data-chapter-id="${chapterId}"]`);
    if (scroller.current && header) scrollContainerTo(scroller.current, header);
  };
  const showScene = (sceneId: string) => {
    const row = scroller.current?.querySelector<HTMLElement>(`[data-scene-id="${sceneId}"]`);
    if (scroller.current && row) scrollContainerTo(scroller.current, row);
  };

  return (
    <SaveCoordinatorContext.Provider value={coordinator}>
      <SceneRegistryContext.Provider value={registry}>
        <MarkerCardContext.Provider value={openCard}>
          <div data-testid={writeTestIds.page} className="flex h-full min-h-0 flex-col">
            {/* Return trips from References change the text: not below write (#80). */}
            {canWrite && <ReturnedCitation acts={acts} />}
            {canWrite && <PreparedParaphrase acts={acts} />}
            <SlotFill slot="saveStatus">
              <SaveStatus onReload={() => void reload()} onRetry={retry} />
            </SlotFill>
            <ScopeSelector acts={acts} scope={scope} inScope={visible} onChange={setScope} />
            <FormatMenu
              format={format}
              onChange={changeFormat}
              previewAnchor={previewAnchor(visible)}
              preview={{ open: previewOpen, onChange: setPreviewOpen }}
            />
            <FocusButton focused={focused} onToggle={() => setFocused((value) => !value)} />
            <div data-testid={writeTestIds.toolbar} className="write-toolbar">
              <ChapterJump
                chapters={chapters}
                current={position?.chapterId ?? null}
                onJump={jump}
              />
              {position && (
                <span data-testid={writeTestIds.crumb} className="write-crumb">
                  {position.crumb}
                </span>
              )}
              <HighlightChip key={highlighted} bookId={bookId} scroller={scroller} />
              <span className="flex-1" />
              <FindInManuscript scroller={scroller} scope={scope} headings={headings} />
            </div>
            <div className="flex min-h-0 flex-1">
              <div
                ref={scroller}
                data-testid={writeTestIds.manuscript}
                tabIndex={-1}
                data-width={format.width}
                data-paragraphs={format.paragraphs}
                data-hide-beats={format.hideBeats}
                data-hide-summaries={format.hideSummaries}
                data-underline-names={format.underlineNames}
                className="write-doc relative min-h-0 min-w-0 flex-1 overflow-auto outline-none"
              >
                <div className="px-5 pt-5 pb-[40vh]">
                  {visible.length === 0 ? (
                    <p data-testid={writeTestIds.empty} className="py-20 text-center text-muted">
                      This book has no chapters yet.
                    </p>
                  ) : (
                    visible.map((act) => (
                      <div key={act.act.id}>
                        <div className="write-act-marker">
                          <ActMarker view={act} />
                        </div>
                        {act.chapters.map((chapter) => (
                          <ChapterSection
                            key={chapter.chapter.id}
                            view={chapter}
                            chapterLinks={chapterLinks}
                            onShowOnly={(id) => setScope({ kind: 'chapter', id })}
                          />
                        ))}
                      </div>
                    ))
                  )}
                </div>
              </div>
              <Minimap acts={visible} scroller={scroller} usedScenes={usedScenes} />
              {previewOpen && (
                <PreviewPane
                  bookId={bookId}
                  target={previewTarget(scope, visible, position?.chapterId ?? null)}
                  sceneId={reading}
                  onClose={() => setPreviewOpen(false)}
                  onScene={showScene}
                />
              )}
            </div>
            {card && (
              <MarkerCard
                bookId={bookId}
                marker={card.marker}
                anchor={card.anchor}
                onClose={closeCard}
              />
            )}
          </div>
        </MarkerCardContext.Provider>
      </SceneRegistryContext.Provider>
    </SaveCoordinatorContext.Provider>
  );
}

/** Where the scene being read sits: its chapter, and `Act 2 · Scene 1 of 2`. */
function locate(acts: readonly ActView[], sceneId: string | null) {
  for (const act of acts) {
    for (const chapter of act.chapters) {
      const index = chapter.scenes.findIndex((s) => s.scene.id === sceneId);
      if (index >= 0) {
        return {
          chapterId: chapter.chapter.id,
          crumb: `Act ${act.number} · Scene ${index + 1} of ${chapter.scenes.length}`,
        };
      }
    }
  }
  return null;
}
