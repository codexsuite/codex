import type { Previewer } from 'pagedjs';
import { useEffect, useRef, useState, type RefObject } from 'react';

export interface PagedInput {
  html: string;
  css: string;
}

export interface PagedState {
  status: 'idle' | 'paging' | 'ready' | 'error';
  /** The input these pages were made from. */
  done: PagedInput | null;
  pages: number;
  /** 1-based numbers of the pages that carry a print-issue mark. */
  issuePages: number[];
  error: string | null;
}

const IDLE: PagedState = { status: 'idle', done: null, pages: 0, issuePages: [], error: null };

// Flows `input` into pages (Paged.js) inside `host`. Each run paginates into a hidden staging element and swaps it in
// when done, so the pages on screen stay until the new ones exist; a newer input discards an unfinished run.
export function usePagedPreview(
  host: RefObject<HTMLDivElement | null>,
  input: PagedInput | null,
): PagedState {
  const [state, setState] = useState<PagedState>(IDLE);
  const live = useRef<Previewer | null>(null);
  const html = input?.html;
  const css = input?.css;

  useEffect(() => {
    const element = host.current;
    if (element === null || html === undefined || css === undefined) return;
    let stale = false;
    const staging = document.createElement('div');
    staging.className = 'layout-staging';
    // Paged.js measures the flow as laid out: under the host's zoom it would break pages in the wrong places and drop
    // text, so the staging element cancels that zoom while it pages.
    const zoom = Number.parseFloat(getComputedStyle(element).zoom) || 1;
    staging.style.zoom = String(1 / zoom);
    element.appendChild(staging);
    // Paged.js is large and only Layout needs it: it loads with the first preview.
    let previewer: Previewer | undefined;
    import('pagedjs')
      .then(({ Previewer: Paged }) => {
        previewer = new Paged();
        return previewer.preview(html, [{ 'quick-preview.css': css }], staging);
      })
      .then(() => {
        if (stale) {
          previewer?.polisher.destroy();
          staging.remove();
          return;
        }
        live.current?.polisher.destroy();
        element.querySelectorAll('.layout-live').forEach((old) => old.remove());
        staging.className = 'layout-live';
        staging.style.zoom = '';
        live.current = previewer ?? null;
        const pages = Array.from(staging.querySelectorAll('.pagedjs_page'));
        setState({
          status: 'ready',
          done: { html, css },
          pages: pages.length,
          issuePages: pages.flatMap((page, i) =>
            page.querySelector('.codex-mark') ? [i + 1] : [],
          ),
          error: null,
        });
      })
      .catch((error: unknown) => {
        staging.remove();
        if (!stale) {
          setState({
            ...IDLE,
            status: 'error',
            error: error instanceof Error ? error.message : String(error),
          });
        }
      });
    return () => {
      stale = true;
    };
  }, [host, html, css]);

  useEffect(() => {
    const element = host.current;
    return () => {
      live.current?.polisher.destroy();
      live.current = null;
      element?.replaceChildren();
    };
  }, [host]);

  // Paging until the pages on screen were made from the current input.
  if (input !== null && (state.done?.html !== input.html || state.done.css !== input.css)) {
    return { ...state, status: 'paging' };
  }
  return state;
}
