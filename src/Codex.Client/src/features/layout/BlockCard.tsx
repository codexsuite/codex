import { Link } from 'react-router-dom';
import type { LayoutFigure } from '@codex/core/api/layout';
import { writeLink } from '@codex/core/shell/links';
import { layoutTestIds } from './testIds';

export interface BlockTarget {
  sceneId: string;
  label: string;
  /** Display number for Open in Write. */
  sceneNumber: string | null;
  figure: LayoutFigure | null;
  /** The title of a spell or stat block, which prints from a Knowledge base entry. */
  entry: string | null;
  latex: string | null;
}

// The card a click on a block opens: what the block is, and Open in Write. A figure shows its asset, pixel size,
// placed size and effective dpi for the current template and trim.
export function BlockCard({
  bookId,
  target,
  onClose,
}: {
  bookId: string;
  target: BlockTarget;
  onClose: () => void;
}) {
  const { figure } = target;
  return (
    <aside
      data-testid={layoutTestIds.blockCard}
      className="absolute right-4 bottom-4 z-10 w-72 rounded-md border border-border bg-surface p-3 text-xs shadow-lg"
    >
      <div className="mb-1 flex items-center justify-between">
        <strong>{target.label}</strong>
        <button
          type="button"
          aria-label="Close"
          data-testid={layoutTestIds.blockCardClose}
          onClick={onClose}
        >
          ×
        </button>
      </div>
      {figure !== null && (
        <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-2 gap-y-0.5">
          <dt className="text-muted">Image</dt>
          <dd className="m-0">{figure.filename}</dd>
          <dt className="text-muted">Pixels</dt>
          <dd className="m-0">
            {figure.widthPx} × {figure.heightPx}
          </dd>
          <dt className="text-muted">Placed</dt>
          <dd className="m-0">
            {figure.placedWidthIn} × {figure.placedHeightIn} in ({figure.placement})
          </dd>
          <dt className="text-muted">Effective dpi</dt>
          <dd className={`m-0 ${figure.status === 'low' ? 'text-warn' : ''}`}>
            {figure.effectiveDpi} {figure.status === 'low' ? '(below 300)' : ''}
          </dd>
        </dl>
      )}
      {target.entry !== null && (
        <p className="m-0">Prints from the Knowledge base entry “{target.entry}”.</p>
      )}
      {target.latex !== null && (
        <p className="m-0">This raw LaTeX block is for the print build only: {target.latex}</p>
      )}
      {target.sceneNumber !== null && (
        <Link
          data-testid={layoutTestIds.openInWrite}
          className="mt-2 inline-block text-accent"
          to={writeLink(bookId, { scene: target.sceneNumber })}
        >
          Open in Write
        </Link>
      )}
    </aside>
  );
}
