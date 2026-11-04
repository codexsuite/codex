import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { invalidateOutline } from '@codex/core/api/outline';
import { openerApi, type OpenerKind, type SetOpenerRequest } from '@codex/core/api/openers';
import type { OutlineOpener } from '@codex/core/api/structure';
import { showToast } from '@codex/core/editing/toastStore';
import { ImageDialog } from '../../images/ImageDialog';
import { ImagePicker } from '../../images/ImagePicker';
import { planTestIds } from './testIds';

interface OpenerDialogProps {
  bookId: string;
  kind: OpenerKind;
  id: string;
  /** "Act 2: The Turn" or "Chapter 3: Classes", for the title and the toast. */
  name: string;
  opener: OutlineOpener | null;
  onClose: () => void;
}

const label = 'pt-1 text-sm text-muted';

// Opener art… on an act (a part opener) or a chapter: the full-page image the layout template draws before it, with
// an optional caption. Save sets it, Clear takes it off; both refresh the outline and offer Undo in the toast.
export function OpenerDialog({ bookId, kind, id, name, opener, onClose }: OpenerDialogProps) {
  const queryClient = useQueryClient();
  const [assetId, setAssetId] = useState<string | null>(opener?.assetId ?? null);
  const [caption, setCaption] = useState(opener?.caption ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const previous: SetOpenerRequest = {
    assetId: opener?.assetId ?? null,
    caption: opener?.caption ?? null,
  };
  const apply = async (request: SetOpenerRequest, message: string) => {
    setBusy(true);
    setError(null);
    try {
      await openerApi.set(kind, id, request);
      await invalidateOutline(queryClient, bookId);
      onClose();
      showToast({
        message,
        undo: async () => {
          try {
            await openerApi.set(kind, id, previous);
          } catch (e) {
            showToast({
              message: e instanceof Error ? e.message : 'The change could not be undone.',
            });
          }
          await invalidateOutline(queryClient, bookId);
        },
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The opener could not be saved.');
      setBusy(false);
    }
  };

  return (
    <ImageDialog
      title={`Opener art · ${name}`}
      testId={planTestIds.openerDialog}
      onClose={onClose}
      onSubmit={() =>
        void apply(
          { assetId, caption: caption.trim() === '' ? null : caption.trim() },
          `Opener art set for ${name}`,
        )
      }
      submitLabel="Save"
      submitTestId={planTestIds.openerSave}
      submitDisabled={assetId === null || busy}
      error={error}
      errorTestId={planTestIds.openerError}
      footerStart={
        opener !== null && (
          <button
            type="button"
            data-testid={planTestIds.openerClear}
            disabled={busy}
            onClick={() => void apply({ assetId: null }, `Opener art cleared from ${name}`)}
            className="h-8 cursor-pointer rounded border border-border bg-transparent px-3 text-sm text-danger"
          >
            Clear
          </button>
        )
      }
    >
      <span className={label}>Image</span>
      <div className="flex min-w-0 flex-col gap-1">
        <ImagePicker bookId={bookId} placement="page" value={assetId} onChange={setAssetId} />
        <span className="text-xs text-muted">
          Printed as a full page before the {kind}, with its title, in templates that draw openers.
        </span>
      </div>
      <label htmlFor="plan-opener-caption" className={label}>
        Caption
      </label>
      <input
        id="plan-opener-caption"
        data-testid={planTestIds.openerCaption}
        autoComplete="off"
        placeholder="Optional, under the title"
        value={caption}
        onChange={(e) => setCaption(e.target.value)}
        className="w-full rounded border border-border bg-bg px-2 py-1 text-base text-text"
      />
    </ImageDialog>
  );
}
