import { useQuery } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { parseGrantRole, sharingApi, sharingKey, type GrantRole } from '@codex/core/api/sharing';
import type { Sharing, SharingUser } from '@codex/core/api/types';
import { InfoIcon } from './accessIcons';
import { Avatar, RoleOptions, Who, personRow, roleSelect } from './SharingPeople';
import {
  isCompleteEmail,
  lookupOutcome,
  roleLabels,
  rolesAbove,
  type GrantScope,
} from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';
import { useDebouncedValue } from './useDebouncedValue';

const LOOKUP_DELAY_MS = 400;

interface SharingAddProps {
  bookId: string;
  sharing: Sharing;
  scope: GrantScope;
  title: string;
  onAdd: (user: SharingUser, role: GrantRole) => Promise<boolean>;
}

function Message({ testId, children }: { testId: string; children: ReactNode }) {
  return (
    <p
      data-testid={testId}
      className="m-0 flex items-center gap-1.5 text-xs text-muted [&_b]:font-medium [&_b]:text-text"
    >
      <InfoIcon className="size-[13px]" />
      <span>{children}</span>
    </p>
  );
}

/**
 * Add people by email: one exact lookup per complete address, debounced (`POST …/sharing/lookup`). Nothing is looked
 * up or shown while the field isn't a whole address: no search, no suggestions.
 */
export function SharingAdd({ bookId, sharing, scope, title, onAdd }: SharingAddProps) {
  const [text, setText] = useState('');
  const [picked, setPicked] = useState<{ userId: string; role: GrantRole } | null>(null);
  const typed = text.trim();
  const complete = isCompleteEmail(typed);
  const wanted = complete ? typed.toLowerCase() : '';
  const debounced = useDebouncedValue(wanted, LOOKUP_DELAY_MS);
  const settled = complete && debounced === wanted;
  const lookup = useQuery({
    queryKey: [...sharingKey(bookId), 'lookup', debounced],
    queryFn: ({ signal }) => sharingApi.lookup(bookId, debounced, signal),
    enabled: settled,
    retry: false,
    staleTime: 0,
    gcTime: 0,
  });

  const id = `settings-sharing-email-${scope}`;
  const series = sharing.series?.title ?? '';
  const label =
    scope === 'series'
      ? `Add people to every book in ${series}`
      : sharing.series
        ? `Add people to ${title} only`
        : 'Add people';

  const user = settled ? lookup.data : undefined;
  const outcome = user ? lookupOutcome(sharing, scope, user) : null;
  const choices = outcome?.kind === 'match' ? rolesAbove(outcome.floor) : [];
  const role: GrantRole | undefined =
    user && picked?.userId === user.id && choices.includes(picked.role) ? picked.role : choices[0];

  const add = async () => {
    if (!user || role === undefined || outcome?.kind !== 'match') return;
    if (await onAdd(user, role)) {
      setText('');
      setPicked(null);
    }
  };

  let result: ReactNode = null;
  if (settled && lookup.isFetching) {
    result = (
      <p
        data-testid={settingsTestIds.sharingLooking}
        className="m-0 flex items-center gap-1.5 text-xs text-muted"
      >
        <span className="size-1.5 animate-pulse rounded-full bg-accent" />
        Looking up…
      </p>
    );
  } else if (settled && lookup.error) {
    const error = lookup.error;
    result =
      error instanceof ApiRequestError && error.status === 404 ? (
        <Message testId={settingsTestIds.sharingNoMatch}>No Codex user has that email.</Message>
      ) : error instanceof ApiRequestError && error.code === 'rate_limited' ? (
        <Message testId={settingsTestIds.sharingMessage}>
          That’s a lot of lookups in a short time. Wait a minute, then try the address again.
        </Message>
      ) : (
        <Message testId={settingsTestIds.sharingMessage}>{error.message}</Message>
      );
  } else if (user && outcome) {
    const name = <b>{user.displayName}</b>;
    if (outcome.kind === 'owner') {
      result = (
        <Message testId={settingsTestIds.sharingMessage}>That’s you. You own this book.</Message>
      );
    } else if (outcome.kind === 'has') {
      result = (
        <Message testId={settingsTestIds.sharingMessage}>
          {name} already has {roleLabels[outcome.role]} on{' '}
          {scope === 'series' ? `every book in ${series}` : 'this book'}. Change it in the list
          above.
        </Message>
      );
    } else if (outcome.kind === 'series-edit') {
      result = (
        <Message testId={settingsTestIds.sharingMessage}>
          {name} can already edit every book in {series}. A grant on one book can only add access.
        </Message>
      );
    } else {
      result = (
        <div
          data-testid={settingsTestIds.sharingMatch}
          className="rounded-md border border-border bg-bg px-2.5"
        >
          <div className={personRow}>
            <Avatar name={user.displayName} me={false} />
            <Who name={user.displayName} detail={user.email ?? undefined} />
            <select
              data-testid={settingsTestIds.sharingMatchRole}
              aria-label={`Access for ${user.displayName}`}
              value={role}
              onChange={(e) => setPicked({ userId: user.id, role: parseGrantRole(e.target.value) })}
              className={roleSelect}
            >
              <RoleOptions roles={choices} />
            </select>
            <button
              type="button"
              data-testid={settingsTestIds.sharingAdd}
              onClick={() => void add()}
              className="inline-flex h-6 cursor-pointer items-center rounded-sm border border-accent bg-accent px-2 text-xs font-medium text-accent-fg hover:brightness-[1.07]"
            >
              Add
            </button>
          </div>
          {outcome.floor !== null && (
            <p
              data-testid={settingsTestIds.sharingMatchFloor}
              className="m-0 mb-2 flex items-start gap-1.5 rounded-sm bg-surface-2 px-2 py-1.5 text-xs text-muted [&_b]:font-medium [&_b]:text-text"
            >
              <InfoIcon className="mt-px size-[13px] text-info" />
              <span>
                {name} has {roleLabels[outcome.floor]} through {series} already. Pick a higher role
                for this book.
              </span>
            </p>
          )}
        </div>
      );
    }
  }

  return (
    <div className="mt-1.5 grid gap-1.5 border-t border-border pt-2.5">
      <label htmlFor={id} className="text-sm text-muted">
        {label}
      </label>
      <input
        id={id}
        data-testid={settingsTestIds.sharingEmail}
        type="email"
        inputMode="email"
        autoComplete="off"
        spellCheck={false}
        placeholder="name@example.com"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            void add();
          }
        }}
        className="h-[var(--control-height)] w-full min-w-0 rounded-sm border border-border bg-bg px-2 text-sm text-text focus:border-transparent focus:outline-2 focus:-outline-offset-1 focus:outline-accent"
      />
      <p className="m-0 -mt-0.5 text-xs leading-[1.45] text-muted">
        Type their whole email address. Codex only finds an exact match: there’s no search and no
        suggestions.
      </p>
      <div aria-live="polite" className="empty:hidden">
        {result}
      </div>
    </div>
  );
}
