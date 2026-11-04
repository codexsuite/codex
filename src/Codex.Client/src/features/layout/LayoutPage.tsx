import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useBook } from '../../api/booksQueries';
import {
  layoutApi,
  layoutKey,
  layoutPreviewKey,
  type LayoutOptionValue,
  type LayoutSelection,
} from '@codex/core/api/layout';
import {
  DEFAULT_EXPORT,
  exportsKey,
  preflightKey,
  type ExportRequest,
} from '@codex/core/api/exports';
import { proofListKey } from '@codex/core/api/proofs';
import { useOutline } from '../../api/outlineQueries';
import { ErrorState } from '../../shell/ErrorState';
import { useDeepLink } from '../../shell/useDeepLink';
import { BlockCard, type BlockTarget } from './BlockCard';
import { LayoutChrome } from './LayoutChrome';
import { ExportDialog } from './ExportDialog';
import { LayoutPanel, type PanelTab } from './LayoutPanel';
import { LayoutToolbar, type LayoutMode } from './LayoutToolbar';
import { PageCanvas, type CanvasFocus, type CanvasView } from './PageCanvas';
import { PageRail } from './PageRail';
import { estimatePages, layoutLabel } from './pageEstimate';
import { pageCss } from './pageStyle';
import { preparePreview } from './preparePreview';
import { ProofView, type ProofInfo } from './ProofView';
import {
  defaultScope,
  proofChapter,
  sceneInfos,
  scopeFromAnchor,
  scopeOptions,
} from './sceneLabels';
import { StatusBar } from './StatusBar';
import { layoutTestIds } from './testIds';
import { useExports } from './useExports';
import { useLayoutEvents } from './useLayoutEvents';
import type { PagedState } from './usePagedPreview';
import './layout.css';
import { useCanWrite } from '../../shell/bookAccess';

const NOT_PAGED: PagedState = { status: 'idle', done: null, pages: 0, issuePages: [], error: null };

// The Layout screen: Quick preview of a scene, chapter or the book as pages (the server's pandoc HTML, paginated in the
// browser with the template's preview.css), re-rendered when prose is saved; or Proof, one chapter compiled by the worker
// with the real template and shown as a PDF. `#scene=5.2` / `#chapter=5` pick the scope.
export function LayoutPage() {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const deepLink = useDeepLink();
  const book = useBook(bookId);
  const layout = useQuery({
    queryKey: layoutKey(bookId),
    queryFn: ({ signal }) => layoutApi.state(bookId, { signal }),
  });
  const outline = useOutline(bookId);
  const [chosenScope, setChosenScope] = useState<string | null>(null);
  const [mode, setMode] = useState<LayoutMode>('quick');
  const canWrite = useCanWrite();
  const [proofInfo, setProofInfo] = useState<ProofInfo | null>(null);
  const [view, setView] = useState<CanvasView>('single');
  const [zoom, setZoom] = useState(1);
  const [showIssues, setShowIssues] = useState(true);
  const [paged, setPaged] = useState<PagedState>(NOT_PAGED);
  const [focus, setFocus] = useState<CanvasFocus | null>(null);
  const [card, setCard] = useState<BlockTarget | null>(null);
  const [exportFrom, setExportFrom] = useState<ExportRequest | null>(null);
  const [openTab, setOpenTab] = useState<{ tab: PanelTab; nonce: number }>();
  const exports = useExports(bookId);
  const nonce = useRef(0);
  const pendingFlash = useRef<string | null>(null);

  const anchor = deepLink.ok ? deepLink.link : undefined;
  const scope = useMemo(() => {
    if (chosenScope !== null) return chosenScope;
    if (!outline.data) return null;
    return (
      scopeFromAnchor(outline.data, anchor?.scene, anchor?.chapter) ?? defaultScope(outline.data)
    );
  }, [chosenScope, outline.data, anchor?.scene, anchor?.chapter]);

  const [kind, scopeId] = (scope ?? 'book').split(':');
  const fingerprint = layout.data
    ? JSON.stringify([
        layout.data.template,
        layout.data.trim,
        layout.data.templates
          .find((t) => t.slug === layout.data.template)
          ?.options.map((o) => o.value),
      ])
    : '';
  const proofChapterId = outline.data && scope !== null ? proofChapter(outline.data, scope) : null;
  // A proof needs write (#80): a tab whose role drops goes back to Quick.
  const proofing = canWrite && mode === 'proof' && proofChapterId !== null;
  const preview = useQuery({
    queryKey: [...layoutPreviewKey(bookId), scope, fingerprint],
    queryFn: ({ signal }) =>
      layoutApi.preview(
        bookId,
        { scope: kind as 'book' | 'chapter' | 'scene', scopeId },
        { signal },
      ),
    enabled: layout.isSuccess && scope !== null && !proofing,
    placeholderData: keepPreviousData,
    retry: false,
  });

  const select = useMutation({
    mutationFn: (selection: LayoutSelection) => layoutApi.select(bookId, selection),
    onSuccess: (state) => queryClient.setQueryData(layoutKey(bookId), state),
  });

  useLayoutEvents(bookId, (sceneId) => {
    pendingFlash.current = sceneId;
    void queryClient.invalidateQueries({ queryKey: layoutPreviewKey(bookId) });
    void queryClient.invalidateQueries({ queryKey: proofListKey(bookId) });
    void queryClient.invalidateQueries({ queryKey: preflightKey(bookId) });
  });

  const input = useMemo(
    () =>
      preview.data
        ? {
            html: preparePreview(preview.data.html, preview.data.figures, preview.data.flags),
            css: pageCss(preview.data.css, preview.data.trim),
          }
        : null,
    [preview.data],
  );

  const onProofInfo = useCallback((info: ProofInfo) => {
    setProofInfo((prev) =>
      prev?.pages === info.pages && prev.cached === info.cached && prev.waiting === info.waiting
        ? prev
        : info,
    );
  }, []);

  const onPaged = useCallback((state: PagedState) => {
    setPaged(state);
    if (state.status === 'ready' && pendingFlash.current !== null) {
      setFocus({ sceneId: pendingFlash.current, scroll: false, nonce: ++nonce.current });
      pendingFlash.current = null;
    }
  }, []);

  const scenes = useMemo(
    () => (outline.data ? sceneInfos(outline.data) : new Map()),
    [outline.data],
  );

  const onBlock = (block: HTMLElement) => {
    const sceneId = block.getAttribute('data-scene') ?? '';
    const figureId = block.closest('[id^="fig-"]')?.id.slice('fig-'.length);
    setCard({
      sceneId,
      label: block.getAttribute('data-label') ?? '',
      sceneNumber: scenes.get(sceneId)?.number ?? null,
      figure: preview.data?.figures.find((f) => f.figureId === figureId) ?? null,
      entry: block.matches('.spell, .statblock') ? block.getAttribute('title') : null,
      latex: block.getAttribute('data-latex'),
    });
  };

  const choose = (change: Partial<LayoutSelection>) => {
    if (!layout.data) return;
    select.mutate({ template: layout.data.template, trim: layout.data.trim, ...change });
  };

  const template = layout.data?.templates.find((t) => t.slug === layout.data.template);
  const pages =
    layout.data && template && outline.data
      ? `≈ ${estimatePages(outline.data, template, layout.data.trim)} pages in ${layoutLabel(template, layout.data.trim)}`
      : undefined;
  const chrome = book.data && <LayoutChrome book={book.data} pages={pages} />;
  if (layout.isError) {
    return (
      <>
        {chrome}
        <ErrorState
          testId={layoutTestIds.loadError}
          title="Layout could not be loaded"
          detail={layout.error.message}
        />
      </>
    );
  }
  if (!layout.data) return chrome ?? null;

  return (
    <section data-testid={layoutTestIds.page} className="flex min-h-0 flex-1 flex-col">
      {chrome}
      <LayoutToolbar
        scopes={outline.data ? scopeOptions(outline.data) : []}
        scope={scope ?? 'book'}
        onScope={setChosenScope}
        layout={layout.data}
        mode={proofing ? 'proof' : 'quick'}
        onMode={setMode}
        canProof={proofChapterId !== null}
        onTemplate={(slug) => choose({ template: slug, trim: undefined })}
        onTrim={(trim) => choose({ trim })}
        view={view}
        onView={setView}
        zoom={zoom}
        onZoom={setZoom}
        showIssues={showIssues}
        onShowIssues={setShowIssues}
        onExport={() => setExportFrom(DEFAULT_EXPORT)}
      />
      {template && !template.hasPreviewCss && (
        <p
          data-testid={layoutTestIds.houseBanner}
          className="m-0 bg-surface-2 px-3 py-1 text-xs text-warn"
        >
          {template.notices[0] ??
            'This template has no preview.css, so Quick preview uses a neutral house style.'}
        </p>
      )}
      <div className="flex min-h-0 flex-1">
        {!proofing && <PageRail pages={paged.pages} issuePages={paged.issuePages} />}
        <div className="relative flex min-w-0 flex-1 flex-col">
          {proofing ? (
            <ProofView
              key={`${proofChapterId}:${fingerprint}`}
              bookId={bookId}
              chapterId={proofChapterId}
              zoom={zoom}
              onInfo={onProofInfo}
            />
          ) : (
            <>
              {preview.isError && (
                <ErrorState
                  testId={layoutTestIds.previewError}
                  title="The preview could not be made"
                  detail={preview.error.message}
                />
              )}
              {paged.error !== null && (
                <ErrorState
                  testId={layoutTestIds.previewError}
                  title="The pages could not be laid out"
                  detail={paged.error}
                />
              )}
              <PageCanvas
                input={input}
                view={view}
                zoom={zoom}
                showIssues={showIssues}
                focus={focus}
                onPaged={onPaged}
                onBlock={onBlock}
              />
            </>
          )}
          {!proofing && card && (
            <BlockCard bookId={bookId} target={card} onClose={() => setCard(null)} />
          )}
        </div>
        <LayoutPanel
          bookId={bookId}
          layout={layout.data}
          preview={preview.data}
          sceneInfo={(id) => scenes.get(id)}
          onOption={(key: string, value: LayoutOptionValue) =>
            choose({ options: { [key]: value } })
          }
          onShow={(sceneId) => setFocus({ sceneId, scroll: true, nonce: ++nonce.current })}
          fingerprint={fingerprint}
          exports={exports}
          onExportAgain={(job) =>
            setExportFrom({
              ...DEFAULT_EXPORT,
              bleed: job.bleed ?? 'none',
              cropMarks: job.cropMarks,
              quality: job.quality ?? 'print',
              draft: job.draft,
            })
          }
          openTab={openTab}
        />
      </div>
      {exportFrom !== null && (
        <ExportDialog
          bookId={bookId}
          layout={layout.data}
          initial={exportFrom}
          sceneInfo={(id) => scenes.get(id)}
          onClose={() => setExportFrom(null)}
          onQueue={async (request) => {
            const refused = await exports.queue(request);
            if (refused === null) {
              void queryClient.invalidateQueries({ queryKey: exportsKey(bookId) });
              setOpenTab({ tab: 'exports', nonce: ++nonce.current });
            }
            return refused;
          }}
        />
      )}
      <StatusBar paged={paged} rendering={preview.isFetching} proof={proofing ? proofInfo : null} />
    </section>
  );
}
