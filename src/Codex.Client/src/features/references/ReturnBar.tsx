import type { SourceRow } from '@codex/core/api/referenceSources';
import { refsTestIds } from './testIds';
import type { ReturnMode } from './add/AddSourceForm';

// On the detour from Write with Add source closed: cite the selected source and go back, or go back without one.
export function ReturnBar({ returning, source }: { returning: ReturnMode; source?: SourceRow }) {
  return (
    <div
      data-testid={refsTestIds.returnBar}
      className="flex items-center gap-3 border-b border-accent bg-accent-soft px-4 py-2 text-sm text-text"
    >
      <span className="flex-1">
        {source
          ? `Cite ${source.title} in ${returning.label}`
          : `Pick a source to cite in ${returning.label}`}
      </span>
      {source && (
        <button
          type="button"
          data-testid={refsTestIds.returnCite}
          onClick={() => returning.onDone(source.id, source.title)}
          className="rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface"
        >
          Cite {source.title} and return
        </button>
      )}
      <button
        type="button"
        data-testid={refsTestIds.returnCancel}
        onClick={returning.onCancel}
        className="rounded-sm border border-border bg-surface px-3 py-1 text-sm text-text"
      >
        Cancel
      </button>
    </div>
  );
}
