import { Link } from 'react-router-dom';
import type { LibrarySeries } from '@codex/core/api/types';
import { seriesLink } from '@codex/core/shell/links';
import { testIds } from '../../testIds';
import { wordsLabel } from '@codex/core/features/library/bookFacts';
import { Cover } from './Cover';
import { libraryTestIds } from './testIds';
import { seriesShare } from '@codex/core/features/library/sharedBooks';
import { RoleChip, SharedBy } from './SharedMarks';

const offsets = [
  'left-0 top-0 z-[3]',
  'left-[7px] top-[5px] z-[2] brightness-[.82]',
  'left-[14px] top-[10px] z-[1] brightness-[.66]',
];

// Up to three covers, each offset down and right and darker, with a book-count badge (design/README.md). The
// Series screen's header stack leaves the badge out: its BOOKS stat carries the count.
export function StackCovers({
  series,
  showCount = true,
}: {
  series: LibrarySeries;
  showCount?: boolean;
}) {
  return (
    <span className="relative block h-[calc(var(--cover-width)*1.5+10px)] w-[calc(var(--cover-width)+14px)]">
      {series.books.slice(0, 3).map((book, index) => (
        <Cover
          key={book.id}
          palette={book.coverPalette}
          imageUrl={book.coverUrl}
          title={book.title}
          author={book.authorName}
          className={`absolute ${offsets[index]}`}
        />
      ))}
      {showCount && (
        <span
          data-testid={libraryTestIds.stackCount}
          className="absolute right-0.5 bottom-0.5 z-[4] rounded-[4px] border border-border bg-surface px-[5px] text-[10px] leading-4 font-semibold"
        >
          {series.bookCount}
        </span>
      )}
    </span>
  );
}

export function SeriesStack({ series }: { series: LibrarySeries }) {
  const share = seriesShare(series);
  return (
    <Link
      to={seriesLink(series.id)}
      data-testid={testIds.shell.seriesStack(series.id)}
      className="flex w-[calc(var(--cover-width)+14px)] flex-col gap-[5px] rounded-xs text-text no-underline outline-offset-1 focus-visible:outline-2 focus-visible:outline-accent"
    >
      <StackCovers series={series} />
      <span className="text-2xs font-medium tracking-[.02em] text-muted uppercase">
        Series · {series.bookCount} {series.bookCount === 1 ? 'book' : 'books'}
      </span>
      <span
        data-testid={libraryTestIds.seriesName}
        className="line-clamp-2 text-[12.5px] leading-tight font-semibold"
      >
        {series.name}
      </span>
      <span className="text-xs text-muted">
        {wordsLabel(series.wordCount)} · {series.lastEditedLabel}
      </span>
      {share && (
        <span className="flex flex-col items-start gap-[5px]">
          <SharedBy owner={share.owner} />
          <RoleChip role={share.role} />
        </span>
      )}
    </Link>
  );
}
