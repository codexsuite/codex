import { useEffect, useState, type RefObject } from 'react';

export interface PdfState {
  status: 'idle' | 'loading' | 'ready' | 'error';
  pages: number;
  error: string | null;
}

const IDLE: PdfState = { status: 'idle', pages: 0, error: null };

// Loads the PDF at `url` with pdf.js (the library and its worker only when a proof is first shown) and draws each page
// into the canvases `host` holds: one `<canvas data-page="n">` per page, which gets `data-rendered` once drawn. `scale` 1
// is 96 dpi (actual size). A new url or scale discards an unfinished run.
export function usePdfPages(
  host: RefObject<HTMLDivElement | null>,
  url: string | null,
  scale: number,
): PdfState {
  const [state, setState] = useState<PdfState>(IDLE);

  useEffect(() => {
    const element = host.current;
    if (url === null || element === null) return;
    let cancelled = false;
    const controller = new AbortController();
    const ratio = window.devicePixelRatio || 1;

    const run = async () => {
      setState({ status: 'loading', pages: 0, error: null });
      try {
        const [pdfjs, worker] = await Promise.all([
          import('pdfjs-dist'),
          import('pdfjs-dist/build/pdf.worker.min.mjs?url'),
        ]);
        pdfjs.GlobalWorkerOptions.workerSrc = worker.default;
        const task = pdfjs.getDocument({ url });
        controller.signal.addEventListener('abort', () => void task.destroy());
        const pdf = await task.promise;
        if (cancelled) return;
        element.replaceChildren();
        const canvases: HTMLCanvasElement[] = [];
        for (let n = 1; n <= pdf.numPages; n++) {
          const canvas = document.createElement('canvas');
          canvas.dataset.page = String(n);
          canvas.setAttribute('data-testid', `layout-proof-page-${n}`);
          canvas.className = 'layout-proof-page';
          element.append(canvas);
          canvases.push(canvas);
        }
        setState({ status: 'ready', pages: pdf.numPages, error: null });
        for (let n = 1; n <= pdf.numPages && !cancelled; n++) {
          const page = await pdf.getPage(n);
          const viewport = page.getViewport({ scale: scale * (96 / 72) * ratio });
          const canvas = canvases[n - 1];
          canvas.width = Math.floor(viewport.width);
          canvas.height = Math.floor(viewport.height);
          canvas.style.width = `${Math.floor(viewport.width / ratio)}px`;
          canvas.style.height = `${Math.floor(viewport.height / ratio)}px`;
          await page.render({ canvas, viewport }).promise;
          canvas.dataset.rendered = 'true';
        }
      } catch (error) {
        if (!cancelled) {
          setState({
            status: 'error',
            pages: 0,
            error: error instanceof Error ? error.message : String(error),
          });
        }
      }
    };

    void run();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [host, url, scale]);

  return url === null ? IDLE : state;
}
