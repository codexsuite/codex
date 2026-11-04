import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { planLink } from '@codex/core/shell/links';
import {
  checkArchive,
  checkImages,
  checkSelection,
  imageSources,
  importApi,
  importForm,
  selectImportArchive,
  selectImportFiles,
  withImages,
  type ImportSelection,
} from './importApi';
import { ImportDialogPreview } from './ImportDialogPreview';
import { describePicked, importSources, type ImportSourceId } from './importSources';
import { importTestIds } from './importTestIds';
import { libraryTestIds } from './testIds';
import { useReturnFocus } from './useReturnFocus';

const button = 'h-8 rounded border border-border bg-transparent px-3 text-sm text-text';

// J3 variant. Step 1: pick a source and a file or folder. Step 2: the server's preview of what will be created
// (nothing is written), then Import book creates it in one go and opens the new book's Plan.
export function ImportDialog({ onClose }: { onClose: () => void }) {
  useReturnFocus();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [sourceId, setSourceId] = useState<ImportSourceId>('novelcrafter');
  const [files, setFiles] = useState<File[]>([]);
  const [step, setStep] = useState<1 | 2>(1);
  const [selection, setSelection] = useState<ImportSelection | null>(null);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [runProblem, setRunProblem] = useState<string | null>(null);
  const source = importSources.find((s) => s.id === sourceId) ?? importSources[0];
  const preview = useMutation({ mutationFn: importApi.preview });
  const run = useMutation({
    mutationFn: importApi.run,
    onSuccess: async (summary) => {
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      void navigate(planLink(summary.bookId));
    },
  });
  // While the import runs the dialog stays put: no second POST from Back, Cancel, ×, Esc or the backdrop.
  const busy = run.isPending || run.isSuccess;
  const close = () => {
    if (!busy) onClose();
  };

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  // A folder picker needs the non-standard webkitdirectory attribute, which React's types don't know.
  useEffect(() => {
    const input = inputRef.current;
    if (!input) return;
    if (source.folder) input.setAttribute('webkitdirectory', '');
    else input.removeAttribute('webkitdirectory');
  }, [source.folder]);

  const choose = (id: ImportSourceId) => {
    setSourceId(id);
    setFiles([]);
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={libraryTestIds.importDialog}
      aria-labelledby="import-title"
      onCancel={(event) => {
        event.preventDefault();
        close();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) close();
      }}
      className="m-auto w-[min(500px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (step === 1 && files.length > 0) {
            setStep(2);
            run.reset();
            preview.reset();
            // A folder sends only the files the importer reads (the rest are named, so they are listed as skipped); a
            // single-file source sends its file whole.
            setRunProblem(null);
            const picked = source.folder
              ? selectImportFiles(files, source.id)
              : selectImportArchive(files);
            const problem = source.folder ? checkSelection(picked) : checkArchive(picked);
            setSelection(picked);
            setSelectionError(problem);
            if (problem === null) preview.mutate(importForm(source.id, picked));
          }
        }}
      >
        <div className="flex items-center gap-2 border-b border-border px-4 py-3">
          <h3 id="import-title" className="m-0 flex-1 text-md font-semibold">
            Import a book
          </h3>
          <span data-testid={libraryTestIds.importStep} className="text-xs text-muted">
            Step {step} of 2
          </span>
          <button
            type="button"
            data-testid={libraryTestIds.importClose}
            aria-label="Close"
            disabled={busy}
            onClick={close}
            className="border-0 bg-transparent text-lg text-muted"
          >
            ×
          </button>
        </div>
        {step === 1 ? (
          <div className="flex flex-col gap-3 p-4">
            <div role="radiogroup" aria-label="Import from" className="flex flex-col gap-2">
              {importSources.map((s) => (
                <button
                  key={s.id}
                  type="button"
                  role="radio"
                  aria-checked={s.id === sourceId}
                  data-testid={libraryTestIds.importSource(s.id)}
                  onClick={() => choose(s.id)}
                  className="flex cursor-pointer items-start gap-2 rounded border border-border bg-transparent px-3 py-2 text-left text-text aria-checked:border-accent aria-checked:bg-accent-soft"
                >
                  <span className="flex flex-col">
                    <b className="text-sm">{s.name}</b>
                    <small className="text-xs text-muted">{s.sub}</small>
                  </span>
                </button>
              ))}
            </div>
            <input
              ref={inputRef}
              type="file"
              hidden
              multiple={source.folder}
              accept={source.accept}
              data-testid={libraryTestIds.importFile}
              onChange={(event) => setFiles(Array.from(event.target.files ?? []))}
            />
            {files.length > 0 ? (
              <div className="flex items-center gap-2 rounded border border-border bg-bg px-3 py-2 text-sm">
                <span data-testid={libraryTestIds.importFileName} className="flex-1 truncate">
                  {describePicked(files, source)}
                </span>
                <button
                  type="button"
                  data-testid={libraryTestIds.importRemove}
                  onClick={() => setFiles([])}
                  className="border-0 bg-transparent text-xs text-accent"
                >
                  Remove
                </button>
              </div>
            ) : (
              <button
                type="button"
                data-testid={libraryTestIds.importPicker}
                onClick={() => inputRef.current?.click()}
                className="flex cursor-pointer flex-col items-center gap-1 rounded border border-dashed border-border bg-bg px-3 py-6 text-sm text-text"
              >
                <b>{source.drop}</b>
                <span className="text-xs text-muted">or click to choose</span>
              </button>
            )}
          </div>
        ) : (
          <div className="p-4">
            {selectionError ? (
              <p
                data-testid={importTestIds.selectionError}
                role="alert"
                className="m-0 text-sm text-warn"
              >
                {selectionError}
              </p>
            ) : preview.isPending ? (
              <p data-testid={importTestIds.loading} className="m-0 text-sm text-muted">
                Reading {describePicked(files, source)}…
              </p>
            ) : preview.isError ? (
              <p
                data-testid={importTestIds.previewError}
                role="alert"
                className="m-0 text-sm text-warn"
              >
                {preview.error.message}
              </p>
            ) : preview.data ? (
              <ImportDialogPreview preview={preview.data} sourceName={source.name} />
            ) : null}
            {(run.isError || runProblem !== null) && (
              <p
                data-testid={importTestIds.runError}
                role="alert"
                className="mt-2 mb-0 text-sm text-warn"
              >
                {runProblem ?? run.error?.message}
              </p>
            )}
          </div>
        )}
        <div className="flex items-center gap-2 border-t border-border bg-bg px-4 py-3">
          {step === 2 ? (
            <button
              type="button"
              data-testid={libraryTestIds.importBack}
              disabled={busy}
              onClick={() => setStep(1)}
              className={button}
            >
              Back
            </button>
          ) : (
            <span className="flex-1 text-xs text-muted">Nothing is imported until step 2</span>
          )}
          {step === 2 && <span className="flex-1" />}
          <button
            type="button"
            data-testid={libraryTestIds.importCancel}
            disabled={busy}
            onClick={close}
            className={button}
          >
            Cancel
          </button>
          {step === 2 && (
            <button
              type="button"
              data-testid={importTestIds.run}
              disabled={!preview.data || !selection || busy}
              onClick={() => {
                if (!selection || !preview.data) return;
                // A source that places images sends the ones the preview listed, and only those.
                if (!imageSources.includes(source.id)) {
                  run.mutate(importForm(source.id, selection));
                  return;
                }
                const upload = withImages(selection, files, preview.data.images ?? []);
                const problem = checkImages(upload);
                setRunProblem(problem);
                if (problem === null) run.mutate(importForm(source.id, upload));
              }}
              className="h-8 rounded border-0 bg-accent px-3 text-sm text-white disabled:opacity-60"
            >
              {run.isPending ? 'Importing…' : 'Import book'}
            </button>
          )}
          {step === 1 && (
            <button
              type="submit"
              data-testid={libraryTestIds.importNext}
              disabled={files.length === 0}
              className="h-8 rounded border-0 bg-accent px-3 text-sm text-white disabled:opacity-60"
            >
              Next
            </button>
          )}
        </div>
      </form>
    </dialog>
  );
}
