import { NavLink, Link } from 'react-router-dom';
import type { BookHeader } from '@codex/core/api/types';
import { Cover } from '../features/library/Cover';
import { testIds } from '../testIds';
import type { BookScreen } from '@codex/core/shell/deepLink';
import { libraryLink, seriesLink, bookScreenLink } from '@codex/core/shell/links';
import { UserMenu } from '../features/auth/UserMenu';
import { LayoutBadge } from './LayoutBadge';
import { ReferencesBadge } from './ReferencesBadge';
import { shellTestIds } from './shellTestIds';
import { RoleChip } from './RoleChip';
import { Slot } from './Slots';
import { useBookAccess } from './bookAccess';

// `Plan · Write | References · Layout`. Chat and Review are not rendered (v1). Back and Settings are icon buttons
// before the book's identity, and the bar ends with the user's avatar, as in the mockup. The palette is ⌘K / Ctrl+K
// (and the Library bar's key cap); the book bar has no button for it.
const tabs: { screen: BookScreen; label: string; id: string; afterDivider?: boolean }[] = [
  { screen: 'plan', label: 'Plan', id: testIds.shell.tabPlan },
  { screen: 'write', label: 'Write', id: testIds.shell.tabWrite },
  {
    screen: 'references',
    label: 'References',
    id: testIds.shell.tabReferences,
    afterDivider: true,
  },
  { screen: 'layout', label: 'Layout', id: testIds.shell.tabLayout },
];

const tabClass = ({ isActive }: { isActive: boolean }) =>
  `relative inline-flex h-full items-center px-2.5 text-sm no-underline hover:text-text ${
    isActive
      ? 'font-semibold text-text after:absolute after:inset-x-2 after:-bottom-px after:h-0.5 after:rounded-sm after:bg-accent after:content-[""]'
      : 'font-medium text-muted'
  }`;

// The 22px icon button of the mockup's `.ib`: muted, a surface-2 wash on hover, accent-soft when current.
const iconButtonClass =
  'inline-grid size-[22px] flex-none place-items-center rounded-xs border-0 bg-transparent p-0 text-muted no-underline hover:bg-surface-2 hover:text-text aria-[current=page]:bg-accent-soft aria-[current=page]:text-accent focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-[13px]';

const iconProps = {
  viewBox: '0 0 16 16',
  fill: 'none',
  stroke: 'currentColor',
  'aria-hidden': true,
};

interface BookBarProps {
  book: BookHeader;
  /** The Knowledge base sidebar toggle at the end of the book's identity; only screens that can hide it pass it. */
  sidebar?: { open: boolean; onToggle: () => void };
}

export function BookBar({ book, sidebar }: BookBarProps) {
  const back = book.seriesId === null ? libraryLink() : seriesLink(book.seriesId);
  const { canWrite } = useBookAccess();
  return (
    <header
      data-testid={testIds.shell.bookBar}
      data-shell-region="book-bar"
      className="flex h-[var(--bar-height)] shrink-0 items-center gap-2 border-b border-border bg-surface px-2"
    >
      <Link
        to={back}
        data-testid={testIds.shell.backLink}
        aria-label={book.seriesName === null ? 'Back to Library' : `Back to ${book.seriesName}`}
        title={book.seriesName === null ? 'Back to Library' : `Back to ${book.seriesName}`}
        className={iconButtonClass}
      >
        <svg {...iconProps} strokeWidth="1.6">
          <path d="M10 3 5 8l5 5" />
        </svg>
      </Link>
      <NavLink
        to={bookScreenLink(book.id, 'settings')}
        data-testid={testIds.shell.tabSettings}
        aria-label="Book settings"
        title="Book settings"
        className={iconButtonClass}
      >
        <svg {...iconProps} strokeWidth="1.4">
          <circle cx="8" cy="8" r="2.2" />
          <path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4" />
        </svg>
      </NavLink>
      <div className="flex h-full min-w-0 items-center gap-2 border-r border-border pr-2">
        <Cover
          palette={book.coverPalette}
          imageUrl={book.coverUrl}
          title=""
          testId={shellTestIds.bookCover}
          className="!w-5 !p-0"
        />
        <div className="flex min-w-0 flex-col leading-[1.15]">
          <b
            data-testid={shellTestIds.bookTitle}
            className="max-w-40 truncate text-sm font-semibold text-text"
          >
            {book.title}
          </b>
          <span className="text-2xs whitespace-nowrap text-muted">{book.authorName}</span>
        </div>
        {sidebar && (
          <button
            type="button"
            data-testid={shellTestIds.sidebarToggle}
            aria-label="Toggle sidebar"
            title="Toggle sidebar"
            aria-expanded={sidebar.open}
            onClick={sidebar.onToggle}
            className={`${iconButtonClass} ${sidebar.open ? 'bg-accent-soft text-accent' : ''}`}
          >
            <svg {...iconProps} strokeWidth="1.4">
              <rect x="2" y="3" width="12" height="10" rx="1.5" />
              <path d="M6 3v10" />
            </svg>
          </button>
        )}
      </div>
      <nav aria-label="Mode" className="flex h-full">
        {tabs.map((tab) => (
          <span key={tab.screen} className="flex h-full items-center">
            {tab.afterDivider && <span aria-hidden="true" className="mx-1 h-4 w-px bg-border" />}
            <NavLink
              to={bookScreenLink(book.id, tab.screen)}
              data-testid={tab.id}
              className={tabClass}
            >
              {tab.label}
              {tab.screen === 'references' && <ReferencesBadge book={book} />}
              {tab.screen === 'references' && <Slot name="badge" className="ml-1 inline-flex" />}
              {tab.screen === 'layout' && <LayoutBadge book={book} />}
            </NavLink>
          </span>
        ))}
      </nav>
      <Slot name="scope" className="flex items-center" />
      <span className="flex-1" />
      {/* Below write nothing saves: the role chip takes the save status's place (#80). */}
      <RoleChip />
      {canWrite && <Slot name="saveStatus" className="flex items-center" />}
      <Slot name="format" className="flex items-center" />
      <Slot name="focus" className="flex items-center" />
      <UserMenu />
    </header>
  );
}
