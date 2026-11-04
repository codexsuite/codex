import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type MouseEvent, type RefObject } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import {
  layoutApi,
  layoutKey,
  layoutPreviewKey,
  type LayoutPreview,
  type LayoutSelection,
} from '@codex/core/api/layout';
import { layoutLink } from '@codex/core/shell/links';
import { Link } from 'react-router-dom';
import { PAGE_SIZE, pageCss } from '../layout/pageStyle';
import { preparePreview } from '../layout/preparePreview';
import { PAGES_CLASS } from '../layout/scopeCss';
import { useLayoutEvents } from '../layout/useLayoutEvents';
import { usePagedPreview } from '../layout/usePagedPreview';
import '../layout/layout.css';
import { ChevronDownIcon } from './icons';
import type { PreviewTarget } from './previewScope';
import { writeTestIds } from './testIds';

interface PreviewPaneProps {
  bookId: string;
  /** What to preview (previewScope.ts); null when nothing is in view. */
  target: PreviewTarget | null;
  /** The scene being read in the prose: the pane scrolls to its first page when it changes. */
  sceneId: string | null;
  onClose: () => void;
  /** A click on a page asks the prose to show that scene. */
  onScene: (sceneId: string) => void;
}

const PAD_PX = 16;

// Write's Preview split pane (#13 Part 2 §2): Quick preview of the scene or chapter in the book's template, paged by
// Paged.js on paper-white pages in both themes. It is made again when a scene in it is saved (its prose version
// changes) or when the book's prose changes elsewhere, keeping the old pages and the scroll position until the new
// pages exist. It follows the prose by scene; clicking a page shows that scene in the prose.
export function PreviewPane({ bookId, target, sceneId, onClose, onScene }: PreviewPaneProps) {
  const queryClient = useQueryClient();
  const scroller = useRef<HTMLDivElement>(null);
  const host = useRef<HTMLDivElement>(null);
  const layout = useQuery({
    queryKey: layoutKey(bookId),
    queryFn: ({ signal }) => layoutApi.state(bookId, { signal }),
  });
  const fingerprint = layout.data
    ? JSON.stringify([
        layout.data.template,
        layout.data.trim,
        layout.data.templates
          .find((t) => t.slug === layout.data.template)
          ?.options.map((o) => o.value),
      ])
    : '';
  const request = target?.request;
  const preview = useQuery({
    queryKey: [
      ...layoutPreviewKey(bookId),
      'write',
      request?.scope,
      request?.scopeId,
      fingerprint,
      target?.versions,
    ],
    queryFn: ({ signal }): Promise<LayoutPreview> =>
      request
        ? layoutApi.preview(bookId, request, { signal })
        : Promise.reject(new Error('Nothing to preview.')),
    enabled: layout.isSuccess && request !== undefined,
    placeholderData: keepPreviousData,
    retry: false,
  });
  const select = useMutation({
    mutationFn: (selection: LayoutSelection) => layoutApi.select(bookId, selection),
    onSuccess: (state) => queryClient.setQueryData(layoutKey(bookId), state),
  });

  // Saves from this tab change `target.versions`; this catches the rest (another tab, MCP, a structure change).
  useLayoutEvents(bookId, () => {
    void queryClient.invalidateQueries({ queryKey: layoutPreviewKey(bookId) });
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
  const paged = usePagedPreview(host, input);
  const zoom = useFitWidth(scroller, preview.data?.trim);
  const jumped = useRef<string | null>(null);
  useSceneSync(scroller, host, sceneId, jumped, paged.status === 'ready', paged.done);

  const click = (event: MouseEvent<HTMLDivElement>) => {
    const block = (event.target as HTMLElement).closest<HTMLElement>('[data-scene]');
    const id = block?.getAttribute('data-scene');
    if (!id) return;
    if (id !== sceneId) jumped.current = id;
    onScene(id);
  };

  const template = layout.data?.templates.find((t) => t.slug === layout.data.template);
  const notConfigured =
    preview.error instanceof ApiRequestError && preview.error.code === 'not_configured';
  const failed = layout.error ?? (notConfigured ? null : preview.error);
  const busy = preview.isFetching || paged.status === 'paging';

  return (
    <aside data-testid={writeTestIds.preview} aria-label="Preview" className="write-preview">
      <header className="write-preview-head">
        <b>Preview</b>
        {layout.data && (
          <span className="write-sel min-w-0">
            <select
              aria-label="Layout template"
              data-testid={writeTestIds.previewTemplate}
              value={layout.data.template}
              disabled={select.isPending}
              onChange={(event) => select.mutate({ template: event.target.value })}
            >
              {layout.data.templates.map((t) => (
                <option key={t.slug} value={t.slug}>
                  {t.name}
                </option>
              ))}
            </select>
            <ChevronDownIcon />
          </span>
        )}
        <span className="flex-1" />
        <button
          type="button"
          className="write-ib"
          data-testid={writeTestIds.previewClose}
          aria-label="Close preview"
          title="Close preview (Ctrl+Alt+P)"
          onClick={onClose}
        >
          ×
        </button>
      </header>
      <div className="write-preview-meta">
        {target && (
          <span data-testid={writeTestIds.previewScope} className="min-w-0 truncate">
            {target.label}
          </span>
        )}
        {paged.done !== null && (
          <span data-testid={writeTestIds.previewPages} className="whitespace-nowrap">
            ≈ {paged.pages} {paged.pages === 1 ? 'page' : 'pages'}
          </span>
        )}
        {busy && !notConfigured && !failed && (
          <span data-testid={writeTestIds.previewRendering} className="whitespace-nowrap">
            Updating…
          </span>
        )}
        <span className="flex-1" />
        <Link
          data-testid={writeTestIds.previewInLayout}
          to={layoutLink(bookId, target?.anchor)}
          className="whitespace-nowrap text-accent no-underline hover:underline"
        >
          Open in Layout
        </Link>
      </div>
      {template && !template.hasPreviewCss && (
        <p data-testid={writeTestIds.previewHouse} className="write-preview-note text-warn">
          {template.notices[0] ??
            'This template has no preview.css, so Quick preview uses a neutral house style.'}
        </p>
      )}
      {notConfigured && (
        <div data-testid={writeTestIds.previewUnavailable} className="write-preview-message">
          <p className="m-0 font-semibold text-text">Preview isn’t available on this server</p>
          <p className="m-0">
            Pages are laid out by the print toolchain (pandoc), which isn’t set up here. Your prose
            is unaffected.
          </p>
        </div>
      )}
      {(failed || paged.error !== null) && (
        <div role="alert" data-testid={writeTestIds.previewError} className="write-preview-message">
          <p className="m-0 font-semibold text-text">The preview could not be made</p>
          <p className="m-0">{failed?.message ?? paged.error}</p>
        </div>
      )}
      {target === null && <div className="write-preview-message">Nothing to preview yet.</div>}
      <div
        ref={scroller}
        data-testid={writeTestIds.previewCanvas}
        className="write-preview-canvas"
        hidden={notConfigured}
      >
        <div
          ref={host}
          role="presentation"
          onClick={click}
          style={{ zoom }}
          className={`${PAGES_CLASS} layout-single`}
        />
      </div>
    </aside>
  );
}

// The zoom that fits a page of `trim` across the pane, never above 100%. The page's width comes from the trim (the
// @page size pageCss sets), not from the pages, whose measured size the zoom itself changes.
function useFitWidth(scroller: RefObject<HTMLDivElement | null>, trim: string | undefined): number {
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const element = scroller.current;
    if (!element) return;
    const observer = new ResizeObserver(() => setWidth(element.clientWidth));
    observer.observe(element);
    return () => observer.disconnect();
  }, [scroller]);
  const page = pageWidthPx(trim);
  if (width === 0 || page === null) return 1;
  return Math.min(1, (width - 2 * PAD_PX) / page);
}

const CSS_PX: Record<string, number> = { in: 96, mm: 96 / 25.4 };

function pageWidthPx(trim: string | undefined): number | null {
  const match = /^([\d.]+)(in|mm)\b/.exec(PAGE_SIZE[trim ?? ''] ?? '');
  return match ? Number(match[1]) * CSS_PX[match[2]] : null;
}

// Scrolls the pane to the first page of the scene being read whenever that scene changes, unless the change came from
// a click on these pages (`jumped`). A scene not on these pages yet (the pane is still making the next chapter) waits
// for the next pages. A re-render of the same pages after an edit does not move the pane.
function useSceneSync(
  scroller: RefObject<HTMLDivElement | null>,
  host: RefObject<HTMLDivElement | null>,
  sceneId: string | null,
  jumpedRef: RefObject<string | null>,
  ready: boolean,
  done: unknown,
) {
  const seen = useRef<string | null>(null);
  const pending = useRef<string | null>(null);
  useEffect(() => {
    if (sceneId !== seen.current) {
      seen.current = sceneId;
      pending.current = sceneId === jumpedRef.current ? null : sceneId;
      jumpedRef.current = null;
    }
    const container = scroller.current;
    const wanted = pending.current;
    if (!ready || !container || wanted === null) return;
    const block = host.current?.querySelector<HTMLElement>(`[data-scene="${CSS.escape(wanted)}"]`);
    if (!block) return;
    pending.current = null;
    // The scene's first page, from its top; or the scene's first block, when it starts too far down that page to show.
    const top = container.getBoundingClientRect().top;
    const page = block.closest<HTMLElement>('.pagedjs_page') ?? block;
    const pageTop = page.getBoundingClientRect().top - top;
    const blockTop = block.getBoundingClientRect().top - top;
    const offset = blockTop - pageTop > container.clientHeight / 2 ? blockTop - PAD_PX : pageTop;
    container.scrollTo({ top: container.scrollTop + offset - PAD_PX });
  }, [scroller, host, sceneId, jumpedRef, ready, done]);
}
