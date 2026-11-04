import type { PublicationField } from '@codex/core/api/settings';
import { settingsTestIds } from './testIds';

interface SourceChipProps {
  field: PublicationField;
  inSeries: boolean;
  inherited: boolean;
  onReset: (field: PublicationField) => void;
}

// After a label in a series book: "Series" (muted) while the value comes from the series, "This book ×" (accent)
// once the book has its own, where the × goes back to the series value.
export function SourceChip({ field, inSeries, inherited, onReset }: SourceChipProps) {
  if (!inSeries) return null;
  if (inherited) {
    return (
      <span
        data-testid={settingsTestIds.chip(field)}
        data-source="series"
        className="ml-1 rounded bg-border px-1 text-2xs uppercase tracking-wide text-muted"
      >
        Series
      </span>
    );
  }
  return (
    <button
      type="button"
      data-testid={settingsTestIds.chipReset(field)}
      data-source="book"
      aria-label="Use the series value"
      onClick={() => onReset(field)}
      className="ml-1 cursor-pointer rounded border-0 bg-accent-soft px-1 text-2xs uppercase tracking-wide text-accent"
    >
      <span data-testid={settingsTestIds.chip(field)}>This book ×</span>
    </button>
  );
}
