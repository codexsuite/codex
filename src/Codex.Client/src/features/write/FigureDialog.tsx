import { useState } from 'react';
import { placementLabels, placements, type Placement } from '@codex/core/api/assets';
import { ImageDialog } from '../../images/ImageDialog';
import { ImagePicker } from '../../images/ImagePicker';
import type { FigureFields } from './figureApi';
import { writeTestIds } from './testIds';

interface FigureDialogProps {
  bookId: string;
  mode: 'insert' | 'edit';
  initial?: Partial<FigureFields>;
  onClose: () => void;
  /** Resolves with the server's refusal, or null once done (the caller closes the dialog). */
  onSubmit: (fields: FigureFields) => Promise<string | null>;
}

const field = 'w-full rounded border border-border bg-bg px-2 py-1 text-base text-text';
const label = 'pt-1 text-sm text-muted';

// Insert figure… (from `/` in Write) and the figure card's Edit: the image (the book's and its series', or a new
// upload), where it sits on the page, its caption and its alt text.
export function FigureDialog({ bookId, mode, initial = {}, onClose, onSubmit }: FigureDialogProps) {
  const [assetId, setAssetId] = useState<string | null>(initial.assetId ?? null);
  const [placement, setPlacement] = useState<Placement>(initial.placement ?? 'column');
  const [caption, setCaption] = useState(initial.caption ?? '');
  const [altText, setAltText] = useState(initial.altText ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (assetId === null) return;
    setBusy(true);
    setError(null);
    const refused = await onSubmit({ assetId, placement, caption, altText });
    if (refused !== null) {
      setError(refused);
      setBusy(false);
    }
  };

  return (
    <ImageDialog
      title={mode === 'insert' ? 'Insert figure' : 'Edit figure'}
      testId={writeTestIds.figureDialog}
      onClose={onClose}
      onSubmit={() => void submit()}
      submitLabel={mode === 'insert' ? 'Insert figure' : 'Save'}
      submitTestId={writeTestIds.figureDialogSubmit}
      submitDisabled={assetId === null || busy}
      error={error}
      errorTestId={writeTestIds.figureDialogError}
    >
      <span className={label}>Image</span>
      <ImagePicker bookId={bookId} placement={placement} value={assetId} onChange={setAssetId} />
      <span className={label}>Placement</span>
      <div role="radiogroup" aria-label="Placement" className="flex flex-wrap">
        {placements.map((p) => (
          <button
            key={p}
            type="button"
            role="radio"
            aria-checked={placement === p}
            data-testid={writeTestIds.figurePlacement(p)}
            onClick={() => setPlacement(p)}
            className={`-ml-px h-[var(--control-height)] cursor-pointer border border-border px-2 text-xs first:ml-0 first:rounded-l-sm last:rounded-r-sm ${
              placement === p
                ? 'relative bg-accent-soft font-medium text-text'
                : 'bg-surface text-muted hover:text-text'
            }`}
          >
            {placementLabels[p]}
          </button>
        ))}
      </div>
      <label htmlFor="write-figure-caption" className={label}>
        Caption
      </label>
      <input
        id="write-figure-caption"
        data-testid={writeTestIds.figureCaptionInput}
        autoComplete="off"
        placeholder="Printed under the image"
        value={caption}
        onChange={(e) => setCaption(e.target.value)}
        className={field}
      />
      <label htmlFor="write-figure-alt" className={label}>
        Alt text
      </label>
      <div className="flex flex-col gap-1">
        <textarea
          id="write-figure-alt"
          data-testid={writeTestIds.figureAltInput}
          rows={2}
          placeholder="What the image shows"
          value={altText}
          onChange={(e) => setAltText(e.target.value)}
          className={`${field} resize-y`}
        />
        <span className="text-xs text-muted">Read out by screen readers and kept in exports.</span>
      </div>
    </ImageDialog>
  );
}
