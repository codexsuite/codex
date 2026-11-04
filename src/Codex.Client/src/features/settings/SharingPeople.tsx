import type { ReactNode } from 'react';
import { grantRoles, parseGrantRole, type GrantRole } from '@codex/core/api/sharing';
import type { Sharing, SharingGrant } from '@codex/core/api/types';
import { LockIcon } from './accessIcons';
import {
  initials,
  otherAccess,
  roleLabels,
  type GrantScope,
} from '@codex/core/features/settings/sharingRules';
import { settingsTestIds } from './testIds';

export const personRow =
  'grid min-h-[42px] grid-cols-[26px_minmax(0,1fr)_auto_auto] items-center gap-x-2.5 gap-y-1 py-1.5 [&+&]:border-t [&+&]:border-dashed [&+&]:border-border';

export const roleSelect =
  'h-[26px] min-w-0 cursor-pointer rounded-sm border border-border bg-bg pr-1 pl-2 text-xs text-text focus:border-transparent focus:outline-2 focus:-outline-offset-1 focus:outline-accent';

export function Avatar({ name, me }: { name: string; me: boolean }) {
  return (
    <span
      aria-hidden="true"
      className={`grid size-[26px] place-items-center rounded-full text-2xs font-semibold ${me ? 'bg-accent-soft text-accent' : 'bg-surface-2 text-text'}`}
    >
      {initials(name)}
    </span>
  );
}

export function Who({
  name,
  you,
  suffix,
  detail,
}: {
  name: string;
  you?: boolean;
  suffix?: string;
  detail?: ReactNode;
}) {
  return (
    <span className="min-w-0">
      <b className="flex items-center gap-1.5 text-sm font-medium whitespace-nowrap">
        <span className="overflow-hidden text-ellipsis">{name}</span>
        {suffix && <em className="font-normal text-muted not-italic">{suffix}</em>}
        {you && (
          <span
            data-testid={settingsTestIds.sharingYou}
            className="inline-flex h-4 flex-none items-center rounded-full bg-accent-soft px-[5px] text-2xs font-semibold text-accent"
          >
            You
          </span>
        )}
      </b>
      {detail && (
        <span className="block overflow-hidden text-xs text-ellipsis whitespace-nowrap text-muted">
          {detail}
        </span>
      )}
    </span>
  );
}

export function RoleOptions({ roles = grantRoles }: { roles?: readonly GrantRole[] }) {
  return roles.map((role) => (
    <option key={role} value={role}>
      {roleLabels[role]}
    </option>
  ));
}

/** The owner first: You · Owner (or their name). Never removable or changeable; the email only for the owner. */
export function OwnerRow({ sharing, meId }: { sharing: Sharing; meId: string | undefined }) {
  const you = sharing.owner.id === meId;
  return (
    <li data-testid={settingsTestIds.sharingOwner} className={personRow}>
      <Avatar name={sharing.owner.displayName} me={you} />
      <Who
        name={you ? 'You' : sharing.owner.displayName}
        suffix="· Owner"
        detail={sharing.owner.email ?? undefined}
      />
      <span
        title="The owner can’t be removed or changed"
        className="inline-grid w-[22px] place-items-center text-muted"
      >
        <LockIcon className="size-3" />
      </span>
      <span />
    </li>
  );
}

interface PersonRowProps {
  sharing: Sharing;
  scope: GrantScope;
  grant: SharingGrant;
  isOwner: boolean;
  meId: string | undefined;
  onRole: (role: GrantRole) => void;
  onRemove: () => void;
}

/** One grant. The owner gets the role menu and Remove; everyone else reads the role. */
export function PersonRow({
  sharing,
  scope,
  grant,
  isOwner,
  meId,
  onRole,
  onRemove,
}: PersonRowProps) {
  const role = parseGrantRole(grant.role);
  const mine = grant.user.id === meId;
  const extra = otherAccess(sharing, scope, grant);
  const detail = [grant.user.email, extra].filter(Boolean).join(' · ');
  return (
    <li data-testid={settingsTestIds.sharingPerson} className={personRow}>
      <Avatar name={grant.user.displayName} me={mine} />
      <Who name={grant.user.displayName} you={mine} detail={detail || undefined} />
      {isOwner ? (
        <>
          <select
            data-testid={settingsTestIds.sharingRole}
            aria-label={`Access for ${grant.user.displayName}`}
            value={role}
            onChange={(e) => onRole(parseGrantRole(e.target.value))}
            className={roleSelect}
          >
            <RoleOptions />
          </select>
          <button
            type="button"
            data-testid={settingsTestIds.sharingRemove}
            aria-label={`Remove ${grant.user.displayName}`}
            onClick={onRemove}
            className="inline-flex h-[22px] cursor-pointer items-center rounded-xs border-0 bg-transparent px-1.5 text-xs font-medium text-muted hover:bg-surface-2 hover:text-text"
          >
            Remove
          </button>
        </>
      ) : (
        <>
          <span
            data-testid={settingsTestIds.sharingRoleText}
            className="text-xs whitespace-nowrap text-muted"
          >
            {roleLabels[role]}
          </span>
          <span />
        </>
      )}
    </li>
  );
}
