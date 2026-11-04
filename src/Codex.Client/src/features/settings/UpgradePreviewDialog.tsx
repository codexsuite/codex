import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { layoutApi, type LayoutTemplate } from '@codex/core/api/layout';
import { TemplatePage, trimGeometry } from './TemplatePage';
import { settingsTestIds } from './testIds';

interface UpgradePreviewDialogProps {
  bookId: string;
  template: LayoutTemplate;
  versionId: string;
  version: string;
  onClose: () => void;
  onApply: () => void;
  applying: boolean;
}

// Quick preview of the whole book with the newer version (`POST …/layout/preview` with its templateVersionId), before
// the book is re-pinned. Nothing changes until Apply; Keep closes it.
export function UpgradePreviewDialog({
  bookId,
  template,
  versionId,
  version,
  onClose,
  onApply,
  applying,
}: UpgradePreviewDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const preview = useQuery({
    queryKey: ['layout-upgrade-preview', bookId, versionId],
    queryFn: ({ signal }) =>
      layoutApi.preview(bookId, { scope: 'book', templateVersionId: versionId }, { signal }),
    retry: false,
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.layoutPreviewDialog}
      aria-labelledby="settings-upgrade-preview-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(560px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-upgrade-preview-title" className="m-0 text-md font-semibold">
          Previewing {template.name} v{version}
        </h3>
        <p className="m-0 text-xs text-muted">
          Not applied. This book stays pinned to v{template.version} until you apply it; other books
          keep their own version.
        </p>
        <div className="flex justify-center rounded-md bg-bg p-3">
          {preview.data ? (
            <TemplatePage
              testId={settingsTestIds.layoutPreviewPage}
              html={preview.data.html}
              css={preview.data.css}
              page={trimGeometry(preview.data.trim)}
              width={420}
              scrollHeight={420}
              title={`${template.name} v${version} preview`}
            />
          ) : (
            <p
              data-testid={settingsTestIds.layoutPreviewState}
              role={preview.isError ? 'alert' : 'status'}
              className={`m-0 py-10 text-sm ${preview.isError ? 'text-danger' : 'text-muted'}`}
            >
              {preview.isError
                ? `The preview could not be rendered: ${preview.error.message}`
                : 'Rendering…'}
            </p>
          )}
        </div>
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.layoutPreviewKeep}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Keep v{template.version}
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.layoutPreviewApply}
            disabled={applying}
            onClick={onApply}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-base text-white disabled:opacity-50"
          >
            Apply v{version}
          </button>
        </div>
      </div>
    </dialog>
  );
}
