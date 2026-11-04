import { useEffect, useRef, useState } from 'react';
import type { LayoutTemplate } from '@codex/core/api/layout';
import { settingsTestIds } from './testIds';

interface ForkTemplateDialogProps {
  template: LayoutTemplate;
  onClose: () => void;
  onFork: (name: string) => Promise<void>;
}

// Asks for the fork's name (default "<name> (fork)"). The fork is the user's own template at 1.0.0 with the same files;
// Download gives its package to edit, and Upload new version brings the edit back.
export function ForkTemplateDialog({ template, onClose, onFork }: ForkTemplateDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [name, setName] = useState(`${template.name} (fork)`);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await onFork(name.trim());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The template could not be forked.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.layoutForkDialog}
      aria-labelledby="settings-fork-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form
        method="dialog"
        className="flex flex-col gap-3 p-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (!busy && name.trim() !== '') void confirm();
        }}
      >
        <h3 id="settings-fork-title" className="m-0 text-md font-semibold">
          Fork {template.name} v{template.version}
        </h3>
        <label className="flex flex-col gap-1 text-sm text-muted">
          Name
          <input
            data-testid={settingsTestIds.layoutForkName}
            value={name}
            maxLength={120}
            onChange={(e) => setName(e.target.value)}
            className="h-8 rounded border border-border bg-bg px-2 text-base text-text"
          />
        </label>
        <p className="m-0 text-xs text-muted">
          An editable copy under Yours, at v1.0.0. Download it, change the class, wrapper, filters
          or preview.css, and upload it as a new version. Books keep the version they are pinned to.
        </p>
        {error && (
          <p role="alert" className="m-0 text-sm text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.layoutForkCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="submit"
            data-testid={settingsTestIds.layoutForkConfirm}
            disabled={busy || name.trim() === ''}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-base text-white disabled:opacity-50"
          >
            Fork
          </button>
        </div>
      </form>
    </dialog>
  );
}
