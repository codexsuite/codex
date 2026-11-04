import { useEffect, useRef, type FormEvent, type ReactNode } from 'react';
import { imagesTestIds } from './testIds';

interface ImageDialogProps {
  title: string;
  testId: string;
  onClose: () => void;
  onSubmit: () => void;
  submitLabel: string;
  submitTestId: string;
  submitDisabled?: boolean;
  /** Why the last submit failed. */
  error?: string | null;
  errorTestId?: string;
  /** Left of Cancel in the footer: a secondary action such as Clear or Remove. */
  footerStart?: ReactNode;
  children: ReactNode;
}

// The design's dialog for placing an image (design/README.md, Dialog): a modal over a dimmed screen with the title and
// ×, label/field rows, then a footer on --color-bg with Cancel and the primary action. A native <dialog> gives the
// backdrop, Esc and the Tab trap; Enter submits; focus goes back to whatever opened it.
export function ImageDialog({
  title,
  testId,
  onClose,
  onSubmit,
  submitLabel,
  submitTestId,
  submitDisabled = false,
  error = null,
  errorTestId,
  footerStart,
  children,
}: ImageDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const opener = useRef(document.activeElement);
  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    const element = opener.current;
    return () => {
      if (element instanceof HTMLElement && element.isConnected) element.focus();
    };
  }, []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!submitDisabled) onSubmit();
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={testId}
      aria-label={title}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
      className="m-auto w-[min(500px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form onSubmit={submit} noValidate className="flex max-h-[85vh] flex-col">
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 className="m-0 text-md font-semibold">{title}</h3>
          <button
            type="button"
            data-testid={imagesTestIds.dialogClose}
            aria-label="Close"
            onClick={onClose}
            className="cursor-pointer border-0 bg-transparent text-lg text-muted hover:text-text"
          >
            ×
          </button>
        </div>
        <div className="grid min-h-0 grid-cols-[88px_1fr] items-start gap-x-3 gap-y-3 overflow-auto p-4 max-sm:grid-cols-1">
          {children}
        </div>
        {error !== null && (
          <p role="alert" data-testid={errorTestId} className="m-0 px-4 pb-3 text-xs text-danger">
            {error}
          </p>
        )}
        <div className="flex items-center gap-2 border-t border-border bg-bg px-4 py-3">
          <span className="flex flex-1 items-center gap-2">{footerStart}</span>
          <button
            type="button"
            data-testid={imagesTestIds.dialogCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-transparent px-3 text-sm text-text"
          >
            Cancel
          </button>
          <button
            type="submit"
            data-testid={submitTestId}
            disabled={submitDisabled}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-sm text-accent-fg disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitLabel}
          </button>
        </div>
      </form>
    </dialog>
  );
}
