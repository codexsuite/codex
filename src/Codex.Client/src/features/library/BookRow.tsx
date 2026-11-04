import { useNavigate } from 'react-router-dom';
import type { LibraryBook } from '@codex/core/api/types';
import { StatusDot } from '../../shell/StatusDot';
import { testIds } from '../../testIds';
import { openLink } from '@codex/core/features/library/bookFacts';
import { ArchivedActions } from './ArchivedActions';
import { Cover } from './Cover';
import { libraryTestIds } from './testIds';
import { parseBookRole } from '@codex/core/api/bookRole';
import { isShared } from '@codex/core/features/library/sharedBooks';
import { RoleChip } from './SharedMarks';

const cell = 'px-2 py-1 align-middle';

// One list row. Click or Enter opens the book as its tile would.
export function BookRow({ book }: { book: LibraryBook }) {
  const navigate = useNavigate();
  const open = () => void navigate(openLink(book));
  return (
    <tr
      tabIndex={0}
      title={`Open ${book.title}`}
      data-testid={testIds.shell.listRow(book.id)}
      onClick={open}
      onKeyDown={(event) => {
        if (event.key === 'Enter') open();
      }}
      className="cursor-pointer hover:bg-accent-soft focus-visible:outline-2 focus-visible:outline-accent"
    >
      <td className={cell}>
        <span className="flex items-center gap-2">
          <Cover
            palette={book.coverPalette}
            imageUrl={book.coverUrl}
            title=""
            className="!w-6 shrink-0"
          />
          <span className="text-sm font-semibold text-text">{book.title}</span>
          <RoleChip role={parseBookRole(book.role)} />
        </span>
      </td>
      <td
        data-testid={libraryTestIds.ownerCell}
        className={`${cell} ${isShared(book) ? 'text-text' : 'text-muted'}`}
      >
        {isShared(book) ? book.owner.displayName : 'You'}
      </td>
      <td className={`${cell} text-muted`}>{book.seriesName ?? '—'}</td>
      <td className={`${cell} text-right text-muted tabular-nums`}>
        {book.seriesNumber === null ? '' : String(book.seriesNumber).padStart(3, '0')}
      </td>
      <td className={`${cell} text-right tabular-nums`}>
        {book.wordCount.toLocaleString('en-US')}
      </td>
      <td className={`${cell} w-28`}>
        {book.progressPct !== null && (
          <span
            data-testid={libraryTestIds.progress}
            className="block h-[3px] overflow-hidden rounded-full bg-surface-2"
          >
            <span
              className="block h-full bg-accent"
              style={{ width: `${Math.min(100, book.progressPct)}%` }}
            />
          </span>
        )}
      </td>
      <td className={cell}>
        <StatusDot status={book.status} />
        {book.editedSincePublication && (
          <span
            data-testid={libraryTestIds.editedSince}
            className="ml-2 text-2xs font-medium text-warn"
          >
            Edited since publication
          </span>
        )}
      </td>
      <td className={`${cell} text-muted`}>{book.lastEditedLabel}</td>
      {book.archived && (
        <td
          className={cell}
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => event.stopPropagation()}
        >
          <ArchivedActions book={book} />
        </td>
      )}
    </tr>
  );
}
