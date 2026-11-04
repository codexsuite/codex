import { Link } from 'react-router-dom';
import { useSharing } from '../../api/sharingQueries';
import { settingsLink } from '@codex/core/shell/links';
import { navSections } from './sections';
import { peopleCount } from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';

const item =
  'flex h-[var(--control-height)] flex-none items-center gap-[7px] rounded-sm px-2 text-sm font-medium whitespace-nowrap text-muted no-underline hover:bg-surface-2 hover:text-text aria-[current=true]:bg-accent-soft aria-[current=true]:font-semibold aria-[current=true]:text-text';

function Icon({ path }: { path: string }) {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      className="size-3.5 flex-none"
    >
      <path d={path} />
    </svg>
  );
}

/** People besides the owner, beside the Sharing entry (the same query the card reads). */
function SharingCount({ bookId }: { bookId: string }) {
  const n = useSharing(bookId).data;
  const count = n ? peopleCount(n) : 0;
  if (count === 0) return null;
  return (
    <span
      data-testid={settingsTestIds.sharingCount}
      title={`${count} ${count === 1 ? 'person' : 'people'} besides the owner`}
      className="ml-auto inline-grid h-4 min-w-4 place-items-center rounded-full bg-surface-2 px-1 text-2xs font-semibold text-muted tabular-nums"
    >
      {count}
    </span>
  );
}

function navFoot(isOwner: boolean, canEdit: boolean, ownerName: string): string {
  if (isOwner) return 'Changes save as you go. Moving the book to another series asks first.';
  return canEdit
    ? `Changes save as you go. ${ownerName} owns this book, so sharing, moving and archiving are theirs.`
    : `You can read these settings. ${ownerName} owns this book.`;
}

// The page title, the sections and a note about autosave. Sticky beside the cards; below 860px a scrolling strip
// above them (the title and the note are hidden there). `active` follows the scroll.
export function SettingsNav({ bookId, active }: { bookId: string; active: string }) {
  const access = useSettingsAccess(bookId);
  return (
    <nav
      data-testid={settingsTestIds.nav}
      aria-label="Settings sections"
      className="sticky top-0 z-10 -mx-4 flex gap-0.5 self-start overflow-x-auto border-b border-border bg-bg px-4 py-1.5 [scrollbar-width:none] min-[860px]:top-4 min-[860px]:mx-0 min-[860px]:grid min-[860px]:gap-px min-[860px]:overflow-visible min-[860px]:border-b-0 min-[860px]:p-0"
    >
      <h1 className="m-0 mb-2 ml-2 hidden text-lg font-semibold text-text min-[860px]:block">
        Book settings
      </h1>
      {navSections.map(({ section, label, icon }) => {
        return (
          <Link
            key={section}
            to={settingsLink(bookId, section)}
            data-testid={settingsTestIds.navItem(section)}
            aria-current={active === section ? 'true' : 'false'}
            className={item}
          >
            <Icon path={icon} />
            {label}
            {section === 'sharing' && <SharingCount bookId={bookId} />}
          </Link>
        );
      })}
      <p
        data-testid={settingsTestIds.navFoot}
        className="m-0 mx-2 mt-3 hidden text-xs leading-[1.45] text-muted min-[860px]:block"
      >
        {navFoot(access.isOwner || !access.known, access.canEdit, access.ownerName)}
      </p>
    </nav>
  );
}
