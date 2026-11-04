import { Link } from 'react-router-dom';
import type { LibraryBook } from '@codex/core/api/types';
import { StatusDot } from '../../shell/StatusDot';
import { testIds } from '../../testIds';
import { openLink, overline, wordsLabel } from '@codex/core/features/library/bookFacts';
import { ArchivedActions } from './ArchivedActions';
import { Cover } from './Cover';
import { libraryTestIds } from './testIds';
import { parseBookRole } from '@codex/core/api/bookRole';
import { isShared } from '@codex/core/features/library/sharedBooks';
import { RoleChip, SharedBy } from './SharedMarks';

// One shelf tile. A link, so click and Enter both open the book. A book shared with you adds who shared it (left out
// inside a series, whose header says it) and your role.
export function BookTile({ book, inSeries = false }: { book: LibraryBook; inSeries?: boolean }) {
  if (book.archived) {
    return (
      <div className="flex flex-col gap-1.5">
        <BookTileLink book={book} inSeries={inSeries} />
        <ArchivedActions book={book} />
      </div>
    );
  }
  return <BookTileLink book={book} inSeries={inSeries} />;
}

function BookTileLink({ book, inSeries }: { book: LibraryBook; inSeries: boolean }) {
  const above = overline(book.seriesNumber);
  return (
    <Link
      to={openLink(book)}
      data-testid={testIds.shell.tile(book.id)}
      className="flex w-[var(--cover-width)] flex-col gap-[5px] rounded-xs text-text no-underline outline-offset-1 hover:[&>:first-child]:outline-2 hover:[&>:first-child]:outline-accent focus-visible:outline-2 focus-visible:outline-accent"
    >
      <Cover
        palette={book.coverPalette}
        imageUrl={book.coverUrl}
        title={book.title}
        author={book.authorName}
      />
      {above && (
        <span className="text-2xs font-medium tracking-[.02em] text-muted uppercase">{above}</span>
      )}
      <span className="line-clamp-2 text-[12.5px] leading-tight font-semibold">{book.title}</span>
      {book.progressPct !== null && (
        <span
          data-testid={libraryTestIds.progress}
          className="h-[3px] overflow-hidden rounded-full bg-surface-2"
        >
          <span
            className="block h-full bg-accent"
            style={{ width: `${Math.min(100, book.progressPct)}%` }}
          />
        </span>
      )}
      <span className="text-xs text-muted">
        {wordsLabel(book.wordCount)} · {book.lastEditedLabel}
      </span>
      <StatusDot status={book.status} />
      {book.editedSincePublication && (
        <span data-testid={libraryTestIds.editedSince} className="text-2xs font-medium text-warn">
          Edited since publication
        </span>
      )}
      {isShared(book) && (
        <span
          data-testid={libraryTestIds.sharedTile}
          className="flex flex-col items-start gap-[5px]"
        >
          {!inSeries && <SharedBy owner={book.owner.displayName} />}
          <RoleChip role={parseBookRole(book.role)} />
        </span>
      )}
    </Link>
  );
}
