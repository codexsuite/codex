import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { bookKey } from '@codex/core/api/books';
import { settingsApi, settingsKey, type SeriesMoveEnd } from '@codex/core/api/settings';
import { showToast } from '@codex/core/editing/toastStore';
import { padded } from '@codex/core/features/settings/seriesNumbers';
import { PeopleIcon } from './accessIcons';
import { plural } from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface ReviewMoveDialogProps {
  bookId: string;
  /** The series to move into; null leaves the series. */
  target: string | null;
  onClose: () => void;
  onMoved: () => void;
}

const endLabel = (end: SeriesMoveEnd) =>
  end.name === null
    ? 'Standalone'
    : `${end.name}${end.number === null ? '' : ` · Book ${padded(end.number)}`}`;

// Sharing follows the series (#80): who will see the book after the move, and who won't any more.
function PeopleLine({
  testId,
  tone,
  children,
}: {
  testId: string;
  tone: 'info' | 'warn';
  children: ReactNode;
}) {
  return (
    <p data-testid={testId} className="m-0 flex items-start gap-2 text-sm [&_b]:font-medium">
      <PeopleIcon
        className={`mt-[3px] size-3.5 flex-none ${tone === 'info' ? 'text-info' : 'text-warn'}`}
      />
      <span>{children}</span>
    </p>
  );
}

// Modal: From -> To and every step of the copy-down, then Move book. Nothing is written until it is confirmed.
export function ReviewMoveDialog({ bookId, target, onClose, onMoved }: ReviewMoveDialogProps) {
  const queryClient = useQueryClient();
  useReturnFocus();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const preview = useQuery({
    queryKey: ['settings', bookId, 'move-preview', target],
    queryFn: () => settingsApi.previewMove(bookId, { seriesId: target }),
    gcTime: 0,
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      const moved = await settingsApi.move(bookId, { seriesId: target });
      queryClient.setQueryData(settingsKey(bookId), moved);
      await queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      showToast({
        message:
          moved.seriesName === null
            ? `${moved.title} is standalone now.`
            : `${moved.title} moved to ${moved.seriesName}.`,
      });
      onMoved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The book could not be moved.');
      setBusy(false);
    }
  };

  const data = preview.data;
  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.moveDialog}
      aria-labelledby="settings-move-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(560px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-move-title" className="m-0 text-md font-semibold">
          Review move
        </h3>
        {preview.isError && (
          <p
            data-testid={settingsTestIds.moveError}
            role="alert"
            className="m-0 text-sm text-danger"
          >
            {preview.error.message}
          </p>
        )}
        {data && (
          <>
            <p data-testid={settingsTestIds.moveFromTo} className="m-0 text-sm">
              <strong>{endLabel(data.from)}</strong> → <strong>{endLabel(data.to)}</strong>
            </p>
            <ol className="m-0 flex flex-col gap-1 pl-5 text-sm">
              {data.steps.map((step) => (
                <li key={step.key} data-testid={settingsTestIds.moveStep(step.key)}>
                  {step.text}
                </li>
              ))}
            </ol>
            {data.to.name !== null && data.targetSeriesSharedWith > 0 && (
              <PeopleLine testId={settingsTestIds.moveGain} tone="info">
                <b>
                  {plural(data.targetSeriesSharedWith, 'person', 'people')} with access to{' '}
                  {data.to.name} will be able to see this book
                </b>{' '}
                You can change this in Sharing.
              </PeopleLine>
            )}
            {data.from.name !== null && data.losingAccess > 0 && (
              <PeopleLine testId={settingsTestIds.moveLose} tone="warn">
                <b>
                  {plural(data.losingAccess, 'person', 'people')} with access to {data.from.name}{' '}
                  will no longer see this book
                </b>{' '}
                People you shared this book with directly keep their access.
              </PeopleLine>
            )}
            <ul
              data-testid={settingsTestIds.moveNotes}
              className="m-0 flex flex-col gap-1 pl-5 text-sm text-muted"
            >
              {data.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
            {data.warnings.map((warning) => (
              <p
                key={warning}
                data-testid={settingsTestIds.moveWarning}
                className="m-0 rounded border border-warn px-2 py-1 text-sm text-warn"
              >
                {warning}
              </p>
            ))}
          </>
        )}
        {error && (
          <p
            data-testid={settingsTestIds.moveError}
            role="alert"
            className="m-0 text-sm text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.moveCancel}
            onClick={onClose}
            className="h-8 rounded border border-border bg-transparent px-3 text-sm text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.moveConfirm}
            disabled={busy || !data}
            onClick={() => void confirm()}
            className="h-8 rounded border border-accent bg-accent px-3 text-sm text-white disabled:opacity-50"
          >
            Move book
          </button>
        </div>
      </div>
    </dialog>
  );
}
