import type { BookRole } from '@codex/core/api/bookRole';
import { libraryTestIds } from './testIds';

// A shared book's marks (#80): who shared it and your role, as the quiet chip of #26 F-06.
const line = {
  viewBox: '0 0 16 16',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.5,
  'aria-hidden': true,
} as const;

const chips: Record<Exclude<BookRole, 'owner'>, { label: string; why: string; path: string }> = {
  view: {
    label: 'View only',
    why: 'You can read it. Typing and changes are turned off.',
    path: 'M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8zM10 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0z',
  },
  comment: {
    label: 'Comment',
    why: 'You can read it and comment.',
    path: 'M2.5 3.5h11v7h-6l-3 2.5v-2.5h-2z',
  },
  write: {
    label: 'Edit',
    why: 'You can change it. Sharing, moving and archiving stay with the owner.',
    path: 'M10.5 2.5 13.5 5.5 6 13H3v-3z',
  },
};

export function PersonIcon({ className }: { className?: string }) {
  return (
    <svg {...line} className={className}>
      <path d="M10.5 5.5a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0zM3 13.5c.5-2.6 2.5-4 5-4s4.5 1.4 5 4" />
    </svg>
  );
}

/** View only / Comment / Edit; nothing for your own books. */
export function RoleChip({ role }: { role: BookRole }) {
  if (role === 'owner') return null;
  const chip = chips[role];
  return (
    <span
      data-testid={libraryTestIds.roleChip}
      title={chip.why}
      className="inline-flex h-[18px] flex-none items-center gap-1 rounded-full bg-surface-2 px-[7px] text-2xs font-medium whitespace-nowrap text-muted"
    >
      <svg {...line} className="size-2.5">
        <path d={chip.path} />
      </svg>
      {chip.label}
    </span>
  );
}

/** "Shared by Mara Lind", two lines at most. */
export function SharedBy({ owner }: { owner: string }) {
  return (
    <span
      data-testid={libraryTestIds.sharedBy}
      title={`Shared by ${owner}`}
      className="flex min-w-0 items-start gap-1 text-2xs leading-[1.35] text-muted"
    >
      <PersonIcon className="mt-px size-[11px] flex-none" />
      <span className="line-clamp-2 [overflow-wrap:anywhere]">Shared by {owner}</span>
    </span>
  );
}
