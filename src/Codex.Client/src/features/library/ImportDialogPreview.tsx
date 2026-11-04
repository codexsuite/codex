import type { ImportPreview } from './importApi';
import { importTestIds } from './importTestIds';

// (temporary CI check) Step 2 of the Import dialog: what the import will create (count → destination) and what it skips, in warn.
export function ImportDialogPreview({
  preview,
  sourceName,
}: {
  preview: ImportPreview;
  sourceName: string;
}) {
  return (
    <div data-testid={importTestIds.preview} className="flex flex-col gap-3">
      <div className="rounded border border-border">
        <div className="flex flex-col border-b border-border px-3 py-2">
          <b data-testid={importTestIds.previewTitle} className="text-sm">
            {preview.bookTitle}
          </b>
          <span className="text-xs text-muted">
            {preview.words.toLocaleString('en-US')} words · {preview.fileCount} files · from{' '}
            {sourceName}
          </span>
        </div>
        {preview.creates.map((row) => (
          <div
            key={row.item}
            data-testid={importTestIds.previewRow(row.item)}
            className="flex items-baseline gap-2 px-3 py-1 text-sm"
          >
            <span className="w-10 text-right tabular-nums">
              {row.count.toLocaleString('en-US')}
            </span>
            <span className="text-muted">→</span>
            <span className="flex-1">
              {row.item} <small className="text-xs text-muted">{row.destination}</small>
            </span>
          </div>
        ))}
        {preview.skipped.length > 0 && (
          <div data-testid={importTestIds.skipped} className="border-t border-border px-3 py-2">
            <b className="text-xs text-warn">
              {preview.skipped.length} skipped {preview.skipped.length === 1 ? 'item' : 'items'}
            </b>
            <ul className="m-0 max-h-32 list-none overflow-auto p-0">
              {preview.skipped.map((s) => (
                <li
                  key={`${s.path}:${s.reason}`}
                  data-testid={importTestIds.skippedRow}
                  className="truncate text-xs text-warn"
                  title={`${s.path}: ${s.reason}`}
                >
                  {s.path} · {s.reason}
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
      <p className="m-0 text-xs text-muted">
        Each field&apos;s mapping is in the{' '}
        <a
          href="https://github.com/codexsuite/codex/blob/main/docs/import.md"
          target="_blank"
          rel="noopener noreferrer"
          className="text-accent"
        >
          import docs
        </a>
        . Your files are not changed. The new book opens on Plan.
      </p>
    </div>
  );
}
