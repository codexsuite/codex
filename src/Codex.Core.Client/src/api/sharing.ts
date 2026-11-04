import { api } from './client';
import type {
  SetGrantRequest,
  Sharing,
  SharingGrant,
  SharingLookupRequest,
  SharingUser,
} from './types';

// Sharing a book or its series (#80; docs/auth.md -> Sharing). Only the owner may look people up or change grants.

export const sharingKey = (bookId: string) => ['sharing', bookId] as const;

/** `GET /api/books/{id}/sharing`: the owner, the book's grants and its series' grants (emails for the owner only). */
export const getSharing = (bookId: string, signal?: AbortSignal) =>
  api.get<Sharing>(`/api/books/${encodeURIComponent(bookId)}/sharing`, { signal });

/** The grant roles, in order; `owner` is never a grant. */
export const grantRoles = ['view', 'comment', 'write'] as const;
export type GrantRole = (typeof grantRoles)[number];

/** A grant's wire value; the server only sends these three. */
export function parseGrantRole(value: string): GrantRole {
  const found = grantRoles.find((role) => role === value);
  if (found === undefined) {
    throw new Error(`Unknown grant role "${value}"; the server sends view, comment or write.`);
  }
  return found;
}

export const sharingApi = {
  /** Exact, case-insensitive match on a whole address. 404 `not_found` is "no Codex user"; 429 `rate_limited`. */
  lookup: (bookId: string, email: string, signal?: AbortSignal) =>
    api.post<SharingUser>(
      `/api/books/${encodeURIComponent(bookId)}/sharing/lookup`,
      { email } satisfies SharingLookupRequest,
      { signal },
    ),
  /** Adds or changes a grant. `targetId` is the book id, or the series id for the whole-series list. */
  setGrant: (targetId: string, userId: string, role: GrantRole) =>
    api.put<SharingGrant>(
      `/api/books/${encodeURIComponent(targetId)}/sharing/${encodeURIComponent(userId)}`,
      { role } satisfies SetGrantRequest,
    ),
  removeGrant: (targetId: string, userId: string) =>
    api.del<void>(
      `/api/books/${encodeURIComponent(targetId)}/sharing/${encodeURIComponent(userId)}`,
    ),
};
