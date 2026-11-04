import type { KbCategory } from '@codex/core/api/kb';
import { CategoryMenu } from './CategoryMenu';
import { EntryMenu } from './EntryMenu';
import { ArrowDownIcon, ArrowUpIcon, CloseIcon, CollapseIcon, ExpandIcon } from './icons';
import { iconButton, inlineField } from './kbStyles';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface EntryHeaderProps {
  bookId: string;
  entryId: string;
  name: string;
  categoryName: string;
  categoryColour: string | null;
  categories: KbCategory[];
  position: string;
  /** The entries before and after this one in the sidebar's order; null at either end of the list. */
  previousId: string | null;
  nextId: string | null;
  expanded: boolean;
  onName: (name: string) => void;
  /** Leaving the name field; `next` is where focus went. */
  onNameBlur: (next: EventTarget | null) => void;
  onPickCategory: (categoryId: string) => Promise<unknown>;
  onCreateCategory: (name: string, colour: string) => Promise<unknown>;
  onStep: (entryId: string) => void;
  onToggleExpand: () => void;
  onClose: () => void;
  /** The entry was deleted: close without saving. */
  onDeleted: () => void;
  /** Set when this is the book's own entry and the book is in a series. */
  seriesName?: string;
  /** This entry belongs to the series, not the book. */
  inSeries: boolean;
  onPromoted: (merged: boolean) => void;
}

// Category chip, then on the right `5 of 12`, previous / next, a divider, Expand, ⋯ and Close; under them the name,
// edited in place. Save status is not repeated here: the book bar shows it.
export function EntryHeader(props: EntryHeaderProps) {
  // Below write (#80): the name reads, and there is no delete, merge or promote.
  const canWrite = useCanWrite();
  return (
    <header className="shrink-0 px-4 pt-2.5">
      <div className="-mr-1 flex min-w-0 items-center gap-1">
        <CategoryMenu
          categories={props.categories}
          currentName={props.categoryName}
          colour={props.categoryColour}
          onPick={props.onPickCategory}
          onCreate={props.onCreateCategory}
        />
        {props.inSeries && (
          <span
            data-testid={kbTestIds.scopeBadge}
            className="rounded-lg bg-surface-2 px-1.5 text-2xs leading-4 font-semibold text-muted"
          >
            Series
          </span>
        )}
        <span className="flex-1" />
        <span
          data-testid={kbTestIds.position}
          className="px-1 text-xs whitespace-nowrap text-muted tabular-nums"
        >
          {props.position}
        </span>
        <button
          type="button"
          data-testid={kbTestIds.entryPrevious}
          aria-label="Previous entry"
          title="Previous entry"
          disabled={props.previousId === null}
          onClick={() => props.previousId && props.onStep(props.previousId)}
          className={iconButton}
        >
          <ArrowUpIcon />
        </button>
        <button
          type="button"
          data-testid={kbTestIds.entryNext}
          aria-label="Next entry"
          title="Next entry"
          disabled={props.nextId === null}
          onClick={() => props.nextId && props.onStep(props.nextId)}
          className={iconButton}
        >
          <ArrowDownIcon />
        </button>
        <span className="mx-0.5 h-4 w-px shrink-0 bg-border" />
        <button
          type="button"
          data-testid={kbTestIds.entryWindowExpand}
          aria-label={props.expanded ? 'Collapse to window' : 'Expand to full view'}
          title={props.expanded ? 'Collapse to window' : 'Expand to full view'}
          aria-pressed={props.expanded}
          onClick={props.onToggleExpand}
          className={`${iconButton} max-[720px]:hidden`}
        >
          {props.expanded ? <CollapseIcon /> : <ExpandIcon />}
        </button>
        {canWrite && (
          <EntryMenu
            bookId={props.bookId}
            entryId={props.entryId}
            name={props.name}
            onDeleted={props.onDeleted}
            seriesName={props.seriesName}
            onPromoted={props.onPromoted}
          />
        )}
        <button
          type="button"
          data-testid={kbTestIds.entryWindowClose}
          aria-label="Close"
          title="Close (Esc)"
          onClick={props.onClose}
          className={iconButton}
        >
          <CloseIcon />
        </button>
      </div>
      <h2 className="m-0 sr-only" data-testid={kbTestIds.entryWindowTitle}>
        {props.name}
      </h2>
      <input
        type="text"
        data-testid={kbTestIds.entryName}
        aria-label="Entry name"
        placeholder="Entry name"
        readOnly={!canWrite}
        value={props.name}
        onChange={(event) => props.onName(event.target.value)}
        onBlur={(event) => props.onNameBlur(event.relatedTarget)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
        }}
        className={`mt-2 -ml-1 w-[calc(100%+4px)] rounded-xs px-1 py-px text-lg leading-[1.3] font-semibold text-text placeholder:font-normal placeholder:text-muted ${inlineField}`}
      />
    </header>
  );
}
