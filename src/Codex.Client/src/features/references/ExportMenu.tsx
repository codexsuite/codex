import { useState } from 'react';
import { sourcesExportUrl, type ExportFormat } from '@codex/core/api/referenceSources';
import { refsTestIds } from './testIds';

const formats: { format: ExportFormat; label: string }[] = [
  { format: 'bibtex', label: 'BibTeX' },
  { format: 'ris', label: 'RIS' },
  { format: 'csljson', label: 'CSL JSON' },
  { format: 'permissions', label: 'Permissions log' },
];

// Each format is a plain download link: the server builds the file and names it.
export function ExportMenu({ bookId }: { bookId: string }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="relative">
      <button
        type="button"
        aria-expanded={open}
        data-testid={refsTestIds.exportToggle}
        onClick={() => setOpen(!open)}
        className="inline-flex h-[var(--control-height)] cursor-pointer items-center rounded-sm border border-border bg-surface px-2.5 text-sm font-medium whitespace-nowrap text-text hover:bg-surface-2"
      >
        Export
      </button>
      {open && (
        <div className="absolute right-0 z-10 mt-1 flex w-40 flex-col rounded-sm border border-border bg-surface py-1 shadow-raised">
          {formats.map(({ format, label }) => (
            <a
              key={format}
              download
              href={sourcesExportUrl(bookId, format)}
              data-testid={refsTestIds.exportLink(format)}
              className="px-3 py-1 text-sm text-text no-underline hover:bg-surface-2"
            >
              {label}
            </a>
          ))}
        </div>
      )}
    </div>
  );
}
