import { useEffect, useRef } from 'react';
import type { BookSettings } from '@codex/core/api/settings';
import { useReturnFocus } from '../library/useReturnFocus';
import { PeopleIcon } from './accessIcons';
import { nameList } from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';

interface ArchiveDialogProps {
  settings: BookSettings;
  /** People the book is shared with directly: archiving removes their grants (#80). */
  losing: string[];
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}

// Confirms archiving: the book leaves Library and the palette; nothing is deleted.
export function ArchiveDialog({ settings, losing, busy, onClose, onConfirm }: ArchiveDialogProps) {
  useReturnFocus();
  const dialogRef = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.archiveDialog}
      aria-labelledby="settings-archive-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-archive-title" className="m-0 text-md font-semibold">
          Archive {settings.title}?
        </h3>
        <p className="m-0 text-sm">
          It leaves Library and the palette. Its text, Knowledge base, References and exports are
          kept, and you can restore it from Archived in Library at any time.
        </p>
        {settings.seriesName && (
          <p className="m-0 text-xs text-muted">
            {settings.seriesName} keeps its number for it, so the other books aren’t renumbered.
          </p>
        )}
        {losing.length > 0 && (
          <p
            data-testid={settingsTestIds.archiveDialogLoss}
            className="m-0 flex items-start gap-2 rounded-md border border-warn/35 bg-warn/[0.07] px-2.5 py-2 text-xs"
          >
            <PeopleIcon className="mt-px size-3.5 flex-none text-warn" />
            <span>
              {nameList(losing)} {losing.length === 1 ? 'loses' : 'lose'} access. Restoring the book
              doesn’t share it again.
            </span>
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.archiveCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.archiveConfirm}
            disabled={busy}
            onClick={onConfirm}
            className="h-8 cursor-pointer rounded border-0 bg-danger px-3 text-base text-white disabled:opacity-50"
          >
            Archive book
          </button>
        </div>
      </div>
    </dialog>
  );
}
