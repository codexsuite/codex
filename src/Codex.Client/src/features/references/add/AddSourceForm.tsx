import { useRef, useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { addSource, lookupSource, type LookupResult } from '@codex/core/api/referenceLookup';
import type { SourceDetailsRequest } from '@codex/core/api/referenceSources';
import { showToast } from '@codex/core/editing/toastStore';
import { useModalFocus } from '../../../editing/useModalFocus';
import { refsTestIds } from '../testIds';
import { blankDetails, detailsFromLookup } from './detailsFromLookup';
import { detectLookup } from './lookupInput';
import { ManualSourceFields } from './ManualSourceFields';
import { useAddedSource } from './useAddedSource';

const buttonClass = 'rounded-sm px-3 py-1 text-sm font-medium disabled:opacity-50';
const primaryClass = `${buttonClass} bg-accent text-surface`;
const ghostClass = `${buttonClass} border border-border bg-surface text-text`;

/** Add source opened from Write's New source…: it says where it goes back to, and goes back when done. */
export interface ReturnMode {
  /** "Write · Chapter 7 · Scene 1". */
  label: string;
  /** A source was added (or an existing one chosen): go back and cite it. */
  onDone: (sourceId: string, title: string) => void;
  onCancel: () => void;
}

// Add source: paste an ISBN, DOI or URL and it is looked up and added; a match already in the book offers that source
// instead; nothing found (or "Enter by hand") opens the fields. Opened from Write (`returning`), Look up reads Save
// and saving, or choosing the existing source, goes back to Write to cite it; Cancel goes back to the scene. Esc is
// Cancel, Tab stays in the form, and focus goes back to what opened it.
export function AddSourceForm({
  bookId,
  onClose,
  returning,
}: {
  bookId: string;
  onClose: () => void;
  returning?: ReturnMode;
}) {
  const { refresh, open } = useAddedSource(bookId);
  const form = useRef<HTMLDivElement>(null);
  useModalFocus(form);
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<LookupResult | null>(null);
  const [manual, setManual] = useState<SourceDetailsRequest | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const existing = result?.outcome === 'existing' ? result : null;

  const openExisting = async (sourceId: string, title: string) => {
    if (returning) {
      returning.onDone(sourceId, title);
      return;
    }
    onClose();
    open(sourceId);
    showToast({ message: `${title} is already in References` });
  };

  const finish = async (
    created: { id: string | null; existingId: string | null },
    title: string,
  ) => {
    await refresh();
    const sourceId = created.id ?? created.existingId;
    if (returning && sourceId !== null) {
      returning.onDone(sourceId, title);
      return;
    }
    onClose();
    if (created.id !== null) {
      open(created.id);
      showToast({ message: `Added ${title}` });
    } else if (created.existingId !== null) {
      open(created.existingId);
      showToast({ message: `${title} is already in References` });
    }
  };

  const guarded = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await work();
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'That could not be done.');
    } finally {
      setBusy(false);
    }
  };

  const lookUp = (text: string) =>
    guarded(async () => {
      const input = detectLookup(text);
      if (input === null) {
        setError('Enter an ISBN, DOI or web address.');
        return;
      }
      const found = await lookupSource(bookId, input.kind, input.value);
      setResult(null);
      setNotFound(false);
      if (found.outcome === 'found' && found.source !== null) {
        await finish(
          await addSource(
            bookId,
            detailsFromLookup(input.kind, found.value, found.source),
            input.kind,
          ),
          found.source.title,
        );
      } else if (found.outcome === 'not_found') {
        setNotFound(true);
        setManual({
          ...blankDetails(input.kind === 'url' ? 'web' : input.kind === 'doi' ? 'paper' : 'book'),
          [input.kind]: found.value,
        });
      } else {
        setResult(found);
      }
    });

  const save = () =>
    guarded(async () => {
      if (manual === null) return;
      const created = await addSource(bookId, manual, null);
      await finish(created, manual.title);
    });

  return (
    <div
      ref={form}
      role="dialog"
      aria-label="Add a source"
      tabIndex={-1}
      data-testid={refsTestIds.addForm}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        (returning ? returning.onCancel : onClose)();
      }}
      className="outline-none absolute left-0 z-10 mt-1 flex w-[380px] flex-col gap-2 rounded-md border border-border bg-surface p-3 shadow-raised"
    >
      <h4 className="m-0 text-sm font-semibold text-text">Add a source</h4>
      {returning && (
        <p data-testid={refsTestIds.addReturnNote} className="m-0 text-xs text-accent">
          Will return to {returning.label}
        </p>
      )}
      {manual === null ? (
        <>
          <p className="m-0 text-xs text-muted">
            Paste an ISBN, DOI or URL and Codex fills in the details.
          </p>
          <input
            autoFocus
            data-testid={refsTestIds.addInput}
            aria-label="ISBN, DOI or URL"
            placeholder="ISBN, DOI or URL"
            value={query}
            spellCheck={false}
            onChange={(event) => {
              setQuery(event.target.value);
              setResult(null);
              setError(null);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && existing === null) void lookUp(query);
            }}
            className="rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text"
          />
          {existing && (
            <div
              data-testid={refsTestIds.addMatch}
              className="rounded-sm border border-border bg-surface-2 p-2 text-sm"
            >
              <div className="text-xs text-muted">Already in References</div>
              <b data-testid={refsTestIds.addMatchTitle}>{existing.existingTitle}</b>
              <div className="text-xs text-muted">{existing.existingByline}</div>
            </div>
          )}
          {result?.outcome === 'redirect' && result.redirectTo !== null && (
            <div data-testid={refsTestIds.addMoved} className="text-sm text-text">
              That address has moved to {result.redirectTo}.{' '}
              <button
                type="button"
                className="text-accent underline"
                onClick={() => {
                  setQuery(result.redirectTo ?? '');
                  void lookUp(result.redirectTo ?? '');
                }}
              >
                Look that up instead
              </button>
            </div>
          )}
        </>
      ) : (
        <div data-testid={refsTestIds.addManualForm} className="flex flex-col gap-2">
          {notFound && (
            <p data-testid={refsTestIds.addNotFound} className="m-0 text-sm text-text">
              Nothing was found for that, so enter the details by hand.
            </p>
          )}
          <ManualSourceFields details={manual} onChange={setManual} />
        </div>
      )}
      {error && (
        <p role="alert" data-testid={refsTestIds.addError} className="m-0 text-sm text-danger">
          {error}
        </p>
      )}
      <div className="flex items-center gap-2">
        {manual === null && (
          <button
            type="button"
            data-testid={refsTestIds.addManual}
            onClick={() => setManual(blankDetails())}
            className={ghostClass}
          >
            Enter by hand
          </button>
        )}
        <button
          type="button"
          data-testid={refsTestIds.addCancel}
          onClick={returning ? returning.onCancel : onClose}
          className={ghostClass}
        >
          Cancel
        </button>
        <span className="flex-1" />
        {manual !== null ? (
          <button
            type="button"
            data-testid={refsTestIds.addSave}
            disabled={busy || manual.title.trim() === ''}
            onClick={() => void save()}
            className={primaryClass}
          >
            Save
          </button>
        ) : existing ? (
          <button
            type="button"
            data-testid={refsTestIds.addUseExisting}
            onClick={() =>
              void openExisting(existing.existingId ?? '', existing.existingTitle ?? '')
            }
            className={primaryClass}
          >
            Use existing source: {existing.existingTitle}
          </button>
        ) : (
          <button
            type="button"
            data-testid={refsTestIds.addLookup}
            disabled={busy || query.trim() === ''}
            onClick={() => void lookUp(query)}
            className={primaryClass}
          >
            {returning ? 'Save' : 'Look up'}
          </button>
        )}
      </div>
    </div>
  );
}
