import type { BookHeader } from '@codex/core/api/types';
import { SaveStatus } from '../../editing/SaveStatus';
import { SlotFill } from '../../shell/Slots';
import { wordsLabel } from '@codex/core/features/library/bookFacts';
import { layoutTestIds } from './testIds';

// What Layout puts in the book bar: the save status and the book's stats after the tabs. `pages` is the
// "≈ n pages in <template> <trim>" part (pageEstimate.ts), once the layout and outline have loaded.
export function LayoutChrome({ book, pages }: { book: BookHeader; pages?: string }) {
  return (
    <>
      <SlotFill slot="scope">
        <span
          data-testid={layoutTestIds.stats}
          className="ml-2 text-xs whitespace-nowrap text-muted tabular-nums"
        >
          {wordsLabel(book.wordCount)}
          {pages && ` · ${pages}`}
        </span>
      </SlotFill>
      <SlotFill slot="saveStatus">
        <SaveStatus />
      </SlotFill>
    </>
  );
}
