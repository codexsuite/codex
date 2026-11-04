import { statusOptions } from '@codex/core/api/library';
import { settingsTestIds } from './testIds';

const stored = statusOptions.filter((s) => s !== 'archived');

// The Library status dots as a segmented control; a choice saves at once. Publishing (and leaving Published, a new
// edition) is the owner's (#80): for anyone else `ownerOnly` explains why those buttons are off.
export function StatusSegments({
  value,
  onChange,
  ownerOnly,
}: {
  value: string;
  onChange: (status: string) => void;
  ownerOnly?: string;
}) {
  return (
    <div
      role="group"
      aria-label="Status"
      data-testid={settingsTestIds.status}
      className="inline-flex h-[var(--control-height)] max-w-full overflow-x-auto overflow-y-hidden rounded-sm border border-border bg-surface"
    >
      {stored.map((status) => {
        const locked = ownerOnly !== undefined && (status === 'published' || value === 'published');
        return (
          <button
            key={status}
            type="button"
            data-testid={settingsTestIds.statusOption(status)}
            aria-pressed={value === status}
            disabled={locked}
            title={locked ? ownerOnly : undefined}
            onClick={() => onChange(status)}
            className="inline-flex cursor-pointer disabled:cursor-not-allowed disabled:opacity-45 aria-pressed:disabled:opacity-100 items-center gap-[5px] border-0 border-r border-border bg-transparent px-[9px] text-sm whitespace-nowrap text-muted capitalize last:border-r-0 hover:bg-surface-2 aria-pressed:bg-accent-soft aria-pressed:font-semibold aria-pressed:text-text"
          >
            <i
              aria-hidden="true"
              className="size-[6px] rounded-full"
              style={{ background: `var(--status-${status})` }}
            />
            {status}
          </button>
        );
      })}
    </div>
  );
}
