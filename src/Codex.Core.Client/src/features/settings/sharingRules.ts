import { grantRoles, parseGrantRole, type GrantRole } from '../../api/sharing';
import type { Sharing, SharingGrant, SharingUser } from '../../api/types';

// The Sharing card's wording and rules (design/screens/settings.html -> Sharing). Pure: no React.

export const roleLabels: Record<GrantRole, string> = {
  view: 'Can view',
  comment: 'Can comment',
  write: 'Can edit',
};

const verbs: Record<GrantRole, string> = { view: 'view', comment: 'comment', write: 'edit' };

/** Which list a grant is on: this book only, or the whole series (a grant on the series row). */
export type GrantScope = 'book' | 'series';

const rank = (role: GrantRole | null) => (role === null ? 0 : grantRoles.indexOf(role) + 1);

/** A whole address: something@domain.tld. Anything less is never looked up. */
const completeEmail = /^[^\s@]+@[^\s@]+\.[^\s@.]{2,}$/;
export const isCompleteEmail = (text: string) => completeEmail.test(text.trim());

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** "Ana", "Ana and Bo", "Ana, Bo and Cy". */
export function nameList(names: string[]): string {
  return names.length > 1
    ? `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
    : (names[0] ?? '');
}

export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const letters =
    words.length > 1 ? words[0][0] + words[words.length - 1][0] : (words[0] ?? '?').slice(0, 2);
  return letters.toUpperCase();
}

export const grantsOf = (sharing: Sharing, scope: GrantScope): SharingGrant[] =>
  scope === 'book' ? sharing.book : (sharing.series?.grants ?? []);

/** The user's role on one list, or null when they have no grant there. */
export function grantOn(sharing: Sharing, scope: GrantScope, userId: string): GrantRole | null {
  const grant = grantsOf(sharing, scope).find((g) => g.user.id === userId);
  return grant ? parseGrantRole(grant.role) : null;
}

/** Everyone besides the owner who can open the book: the nav badge. */
export function peopleCount(sharing: Sharing): number {
  const ids = new Set([...sharing.book, ...(sharing.series?.grants ?? [])].map((g) => g.user.id));
  return ids.size;
}

/** The second line's note about the other list: "Can edit through the series" / "Can edit on this book". */
export function otherAccess(sharing: Sharing, scope: GrantScope, grant: SharingGrant): string {
  if (sharing.series === null) return '';
  const role = parseGrantRole(grant.role);
  const other = grantOn(sharing, scope === 'book' ? 'series' : 'book', grant.user.id);
  if (other === null) return '';
  if (scope === 'book' && rank(other) >= rank(role))
    return `${roleLabels[other]} through the series`;
  if (scope === 'series' && rank(other) > rank(role)) return `${roleLabels[other]} on this book`;
  return '';
}

/** Roles a new or changed grant may take on this list: above what the series already gives, on the book list. */
export function floorFor(sharing: Sharing, scope: GrantScope, userId: string): GrantRole | null {
  return scope === 'book' ? grantOn(sharing, 'series', userId) : null;
}

export const rolesAbove = (floor: GrantRole | null) =>
  grantRoles.filter((r) => rank(r) > rank(floor));

/** What a found address means on a list: a match to add, or why it can't be added. */
export type LookupOutcome =
  | { kind: 'owner' }
  | { kind: 'has'; role: GrantRole }
  | { kind: 'series-edit' }
  | { kind: 'match'; floor: GrantRole | null };

export function lookupOutcome(
  sharing: Sharing,
  scope: GrantScope,
  user: SharingUser,
): LookupOutcome {
  if (user.id === sharing.owner.id) return { kind: 'owner' };
  const here = grantOn(sharing, scope, user.id);
  if (here !== null) return { kind: 'has', role: here };
  const floor = floorFor(sharing, scope, user.id);
  if (floor === 'write') return { kind: 'series-edit' };
  return { kind: 'match', floor };
}

/** "the book's title" or "every book in <series>", as the toasts and messages say it. */
export function scopeName(sharing: Sharing, scope: GrantScope, title: string): string {
  return scope === 'series' && sharing.series ? `every book in ${sharing.series.title}` : title;
}

export const addedToast = (
  sharing: Sharing,
  scope: GrantScope,
  title: string,
  name: string,
  role: GrantRole,
) => `Shared ${scopeName(sharing, scope, title)} with ${name} · ${roleLabels[role]}`;

export const changedToast = (
  sharing: Sharing,
  scope: GrantScope,
  title: string,
  name: string,
  role: GrantRole,
) => `${name} can now ${verbs[role]} ${scopeName(sharing, scope, title)}`;

/** After a removal: what the person keeps through the other list, or that their tabs go back to Library. */
export function removedToast(sharing: Sharing, scope: GrantScope, user: SharingUser): string {
  const series = sharing.series?.title ?? '';
  const left = grantOn(sharing, scope === 'book' ? 'series' : 'book', user.id);
  const from = scope === 'series' ? ` from ${series}` : '';
  const keeps =
    left === null
      ? 'Their open tabs go back to Library.'
      : `They keep ${roleLabels[left]} ${scope === 'series' ? 'on this book' : `through ${series}`}.`;
  return `Removed ${user.displayName}${from}. ${keeps}`;
}
