import { useEffect, useRef } from 'react';
import { useModalFocus } from '../../../editing/useModalFocus';
import type { SourceDetail } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

// Removing a source archives it; its markers stay in Write as plain text. Asking moves focus to Cancel, Tab stays in the
// question and Esc or Cancel puts focus back on Remove source.
export function RemoveSource({
  source,
  confirming,
  busy,
  onAsk,
  onCancel,
  onConfirm,
}: {
  source: SourceDetail;
  confirming: boolean;
  busy: boolean;
  onAsk: () => void;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const ask = useRef<HTMLButtonElement>(null);
  const wasConfirming = useRef(false);
  useEffect(() => {
    if (wasConfirming.current && !confirming) ask.current?.focus();
    wasConfirming.current = confirming;
  }, [confirming]);

  if (!confirming) {
    return (
      <button
        ref={ask}
        type="button"
        data-testid={refsTestIds.remove}
        onClick={onAsk}
        className="self-start rounded-sm border border-danger px-3 py-1 text-sm text-danger"
      >
        Remove source
      </button>
    );
  }
  return <ConfirmRemove source={source} busy={busy} onCancel={onCancel} onConfirm={onConfirm} />;
}

function ConfirmRemove({
  source,
  busy,
  onCancel,
  onConfirm,
}: {
  source: SourceDetail;
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const dialog = useRef<HTMLDivElement>(null);
  useModalFocus(dialog);
  const markers = source.uses.length;
  return (
    <div
      ref={dialog}
      role="alertdialog"
      aria-label={`Remove ${source.title}`}
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onCancel();
      }}
      className="flex flex-col gap-2 rounded-md border border-danger p-3 text-sm outline-none"
    >
      <p className="m-0 text-text">
        Remove {source.title}?{' '}
        {markers === 0
          ? 'No marker cites it.'
          : `Its ${markers} ${markers === 1 ? 'marker becomes' : 'markers become'} plain text in Write.`}
      </p>
      <div className="flex gap-2">
        <button
          type="button"
          disabled={busy}
          data-testid={refsTestIds.removeConfirm}
          onClick={onConfirm}
          className="rounded-sm bg-danger px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
        >
          Remove
        </button>
        <button
          type="button"
          autoFocus
          data-testid={refsTestIds.removeCancel}
          onClick={onCancel}
          className="rounded-sm border border-border px-3 py-1 text-sm"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
