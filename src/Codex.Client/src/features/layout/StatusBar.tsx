import type { ProofInfo } from './ProofView';
import { layoutTestIds } from './testIds';
import type { PagedState } from './usePagedPreview';

interface StatusBarProps {
  paged: PagedState;
  rendering: boolean;
  /** Set in Proof mode: what the proof shows, instead of the Quick preview's pages. */
  proof: ProofInfo | null;
}

// Quick (live) with the page count and a note while a re-render runs; or Proof with its exact page count.
export function StatusBar({ paged, rendering, proof }: StatusBarProps) {
  if (proof !== null) {
    return (
      <div
        data-testid={layoutTestIds.status}
        className="flex items-center gap-3 border-t border-border bg-surface px-3 py-1 text-xs text-muted"
      >
        <span>Proof</span>
        {proof.pages !== null && (
          <span data-testid={layoutTestIds.statusPages}>
            {proof.pages} {proof.pages === 1 ? 'page' : 'pages'}
          </span>
        )}
        {proof.waiting && <span data-testid={layoutTestIds.statusRendering}>Compiling…</span>}
        {proof.cached && <span>cached</span>}
      </div>
    );
  }
  return (
    <div
      data-testid={layoutTestIds.status}
      className="flex items-center gap-3 border-t border-border bg-surface px-3 py-1 text-xs text-muted"
    >
      <span>Quick · live</span>
      <span data-testid={layoutTestIds.statusPages}>
        {paged.pages} {paged.pages === 1 ? 'page' : 'pages'}
      </span>
      {(rendering || paged.status === 'paging') && (
        <span data-testid={layoutTestIds.statusRendering}>Updating…</span>
      )}
    </div>
  );
}
