import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
  publishKey,
  settingsApi,
  type BookSettings,
  type PublishState,
} from '@codex/core/api/settings';
import { usePublishState } from '../../api/settingsQueries';
import { settingsTestIds } from './testIds';

interface PublishDialogProps {
  settings: BookSettings;
  onClose: () => void;
  /** The book is published and its snapshot queued; the caller syncs the status control. */
  onPublished: (state: PublishState) => void;
}

function riskLine(count: number) {
  if (count === 0) return '0 sources at risk.';
  const sources = count === 1 ? '1 source is' : `${count} sources are`;
  return `${sources} at risk. The book can be marked published, but Export for printer stays blocked.`;
}

// Confirms Published (Book settings -> Status): the words, the sources at risk and the snapshot that is saved.
// Risks warn but never block; a book with no chapters can't be published.
export function PublishDialog({ settings, onClose, onPublished }: PublishDialogProps) {
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const state = usePublishState(settings.id);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const facts = state.data;
  const empty = facts !== undefined && facts.chapterCount === 0;

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      const next = await settingsApi.publish(settings.id);
      queryClient.setQueryData(publishKey(settings.id), next);
      onPublished(next);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The book could not be published.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.publishDialog}
      aria-labelledby="settings-publish-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-publish-title" className="m-0 text-md font-semibold">
          Mark {settings.title} as published?
        </h3>
        {state.isError && (
          <p role="alert" className="m-0 text-sm text-danger">
            {state.error.message}
          </p>
        )}
        {facts && (
          <p data-testid={settingsTestIds.publishMessage} className="m-0 text-sm text-text">
            {empty ? (
              'Nothing to publish. Add a chapter first.'
            ) : (
              <>
                Words: {facts.wordCount.toLocaleString('en-US')}.{' '}
                <span data-testid={settingsTestIds.publishWarning}>
                  {riskLine(facts.atRiskCount)}
                </span>{' '}
                A snapshot of the manuscript is saved.
              </>
            )}
          </p>
        )}
        {facts?.exportRunning && !empty && (
          <p
            data-testid={settingsTestIds.publishWaiting}
            role="status"
            className="m-0 text-xs text-muted"
          >
            Waiting for the running export: the snapshot is built when it finishes.
          </p>
        )}
        {error && (
          <p
            data-testid={settingsTestIds.publishError}
            role="alert"
            className="m-0 text-sm text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.publishCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.publishConfirm}
            disabled={busy || !facts || empty}
            onClick={() => void confirm()}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-base text-white disabled:opacity-50"
          >
            Mark as published
          </button>
        </div>
      </div>
    </dialog>
  );
}
