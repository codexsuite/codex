import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { libraryLink } from '@codex/core/shell/links';
import {
  clearBookImport,
  describeImport,
  importApi,
  sourceNames,
  useBookImport,
} from '../library/importApi';
import { importTestIds } from '../library/importTestIds';
import { ImportBannerConfirm } from './ImportBannerConfirm';
import { useBookAccess } from '../../shell/bookAccess';

// After an import, a dismissible info banner at the top of the board: what came across, what was skipped, and
// Undo import (after a confirm, deletes the book and returns to the Library; offered only until the book is written
// in). Reads the book's import itself by the route's book id.
export function ImportBanner() {
  const { bookId = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const bookImport = useBookImport(bookId);
  const [confirming, setConfirming] = useState(false);
  // Undo import deletes the book (owner only); dismissing is a change (write, #80).
  const { role, canWrite } = useBookAccess();
  const dismiss = useMutation({
    mutationFn: importApi.dismiss,
    onSuccess: () => clearBookImport(queryClient, bookId),
  });
  const undo = useMutation({
    mutationFn: importApi.undo,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      void navigate(libraryLink());
    },
  });
  const summary = bookImport.data?.banner;
  if (!summary) return null;
  const skipped = summary.skipped.length;

  return (
    <div
      data-testid={importTestIds.banner}
      role="status"
      className="mb-2 flex items-start gap-2 rounded-md border border-info bg-info/10 px-3 py-2 text-sm"
    >
      <span aria-hidden className="text-info">
        ⓘ
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <span>
          <b>Imported from {sourceNames[summary.source] ?? summary.source}</b> · {summary.fileName}
        </span>
        <span data-testid={importTestIds.bannerText} className="text-xs text-muted">
          {describeImport(summary)}
        </span>
        {skipped > 0 && (
          <details className="text-xs text-warn">
            <summary data-testid={importTestIds.bannerSkipped} className="cursor-pointer">
              {skipped} {skipped === 1 ? 'item was' : 'items were'} skipped
            </summary>
            <ul className="m-0 mt-1 max-h-32 list-none overflow-auto p-0">
              {summary.skipped.map((s) => (
                <li key={`${s.path}:${s.reason}`} data-testid={importTestIds.bannerSkippedRow}>
                  {s.path} · {s.reason}
                </li>
              ))}
            </ul>
          </details>
        )}
        {(undo.isError || dismiss.isError) && (
          <span data-testid={importTestIds.bannerError} role="alert" className="text-xs text-warn">
            {(undo.error ?? dismiss.error)?.message}
          </span>
        )}
        {role === 'owner' && bookImport.data?.canUndo && (
          <div>
            <button
              type="button"
              data-testid={importTestIds.undo}
              onClick={() => setConfirming(true)}
              className="border-0 bg-transparent p-0 text-xs text-accent"
            >
              Undo import
            </button>
          </div>
        )}
        {confirming && (
          <ImportBannerConfirm
            fileName={summary.fileName}
            busy={undo.isPending || undo.isSuccess}
            onConfirm={() => undo.mutate(summary.importId)}
            onCancel={() => setConfirming(false)}
          />
        )}
      </div>
      {canWrite && (
        <button
          type="button"
          data-testid={importTestIds.dismiss}
          aria-label="Dismiss import summary"
          onClick={() => dismiss.mutate(summary.importId)}
          className="border-0 bg-transparent text-lg leading-none text-muted"
        >
          ×
        </button>
      )}
    </div>
  );
}
