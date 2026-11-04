import { useRef, useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { importSources, previewImport, type ImportResult } from '@codex/core/api/referenceLookup';
import { useModalFocus } from '../../../editing/useModalFocus';
import { refsTestIds } from '../testIds';
import { parseBibtex, type ParsedImport } from './bibtexImport';
import { looksLikeCsl, parseCslJson } from './cslImport';
import { parseRis } from './risImport';
import { useAddedSource } from './useAddedSource';

const buttonClass = 'rounded-sm px-3 py-1 text-sm font-medium disabled:opacity-50';

// A .json file is CSL JSON, an .ris file is RIS and a .bib file is BibTeX; a file with another name is told by its first characters.
export function parseImportFile(name: string, text: string): ParsedImport {
  const lower = name.toLowerCase();
  if (lower.endsWith('.json') || (!/\.(bib|ris)$/.test(lower) && looksLikeCsl(text))) {
    return parseCslJson(text);
  }
  const isRis = lower.endsWith('.ris') || (!lower.endsWith('.bib') && /^\s*TY {2}-/.test(text));
  return isRis ? parseRis(text) : parseBibtex(text);
}

// "2 new · 1 already in References · 1 skipped (no title)": the parts that are zero are left out.
export function importSummary(preview: ImportResult): string {
  const parts = [`${preview.created} new`];
  if (preview.duplicates > 0) parts.push(`${preview.duplicates} already in References`);
  if (preview.untitled > 0) parts.push(`${preview.untitled} skipped (no title)`);
  return parts.join(' · ');
}

// Import: read a CSL JSON, BibTeX or RIS file here, ask the server what it would do, then post the entries.
export function ImportDialog({ bookId, onClose }: { bookId: string; onClose: () => void }) {
  const { refresh } = useAddedSource(bookId);
  const dialog = useRef<HTMLDivElement>(null);
  useModalFocus(dialog);
  const [parsed, setParsed] = useState<ParsedImport | null>(null);
  const [preview, setPreview] = useState<ImportResult | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const choose = async (file: File | undefined) => {
    setResult(null);
    setPreview(null);
    setError(null);
    const next = file ? parseImportFile(file.name, await file.text()) : null;
    setParsed(next);
    if (next === null || next.items.length === 0) return;
    try {
      setPreview(await previewImport(bookId, next.items));
    } catch (caught) {
      setError(
        caught instanceof ApiRequestError ? caught.message : 'The file could not be checked.',
      );
    }
  };

  const submit = async () => {
    if (parsed === null) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await importSources(bookId, parsed.items));
      await refresh();
    } catch (caught) {
      setError(
        caught instanceof ApiRequestError ? caught.message : 'The import could not be done.',
      );
    } finally {
      setBusy(false);
    }
  };

  const skipped = result ?? preview;

  return (
    <div
      ref={dialog}
      role="dialog"
      aria-label="Import sources"
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        onClose();
      }}
      data-testid={refsTestIds.addImportDialog}
      className="absolute left-0 z-10 mt-1 flex w-[380px] outline-none flex-col gap-2 rounded-md border border-border bg-surface p-3 shadow-raised"
    >
      <h4 className="m-0 text-sm font-semibold text-text">Import sources</h4>
      <p className="m-0 text-xs text-muted">
        A CSL JSON (.json, Zotero's export), BibTeX (.bib) or RIS (.ris) file. Sources already in
        References, matched by ISBN, DOI, URL or key, are skipped.
      </p>
      <input
        type="file"
        accept=".json,.bib,.ris,.txt"
        aria-label="File to import"
        data-testid={refsTestIds.addImportFile}
        onChange={(event) => void choose(event.target.files?.[0])}
        className="text-sm text-text"
      />
      {parsed && (
        <div data-testid={refsTestIds.addImportSummary} className="text-sm text-text">
          {preview
            ? importSummary(preview)
            : parsed.items.length === 1
              ? '1 entry found'
              : `${parsed.items.length} entries found`}
        </div>
      )}
      {parsed?.problems.map((problem) => (
        <div key={problem} data-testid={refsTestIds.addImportProblem} className="text-xs text-warn">
          {problem}
        </div>
      ))}
      {result && (
        <div data-testid={refsTestIds.addImportResult} className="text-sm text-text">
          {result.created} added
        </div>
      )}
      {skipped && skipped.skipped > 0 && (
        <ul
          data-testid={refsTestIds.addImportSkipped}
          className="m-0 list-disc pl-5 text-xs text-muted"
        >
          {skipped.skippedTitles.map((title, index) => (
            <li key={`${title}-${index}`}>
              {title}
              {skipped.skippedReasons?.[index] ? `: ${skipped.skippedReasons[index]}` : ''}
            </li>
          ))}
        </ul>
      )}
      {error && (
        <p role="alert" data-testid={refsTestIds.addError} className="m-0 text-sm text-danger">
          {error}
        </p>
      )}
      <div className="flex items-center gap-2">
        <span className="flex-1" />
        <button
          type="button"
          data-testid={refsTestIds.addImportClose}
          onClick={onClose}
          className={`${buttonClass} border border-border bg-surface text-text`}
        >
          {result ? 'Done' : 'Cancel'}
        </button>
        <button
          type="button"
          data-testid={refsTestIds.addImportSubmit}
          disabled={
            busy ||
            result !== null ||
            parsed === null ||
            parsed.items.length === 0 ||
            preview?.created === 0
          }
          onClick={() => void submit()}
          className={`${buttonClass} bg-accent text-surface`}
        >
          Import
        </button>
      </div>
    </div>
  );
}
