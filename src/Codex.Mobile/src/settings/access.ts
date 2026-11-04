import { hasRole, parseBookRole, type BookRole } from '@codex/core/api/bookRole';

export interface SettingsAccess {
  /** False until the book header has said who the caller is: nothing is editable before then. */
  known: boolean;
  role: BookRole;
  isOwner: boolean;
  /** Write or owner: the fields are editable (owner-only actions aside). */
  canEdit: boolean;
}

/** What the caller may do on a book's settings (#80): viewers and commenters read, editors change all but the owner's actions. */
export function accessOf(role: string | undefined): SettingsAccess {
  if (role === undefined) return { known: false, role: 'view', isOwner: false, canEdit: false };
  const parsed = parseBookRole(role);
  return {
    known: true,
    role: parsed,
    isOwner: parsed === 'owner',
    canEdit: hasRole(parsed, 'write'),
  };
}

/** The line a read-only caller sees at the top of the page, or null when they can edit. */
export function readOnlyNote(access: SettingsAccess, owner: string, title: string): string | null {
  if (!access.known || access.canEdit) return null;
  return access.role === 'comment'
    ? `Comment only. ${owner} shared ${title} with you to comment on; its settings are read-only.`
    : `View only. ${owner} shared ${title} with you to read; its settings are read-only.`;
}
