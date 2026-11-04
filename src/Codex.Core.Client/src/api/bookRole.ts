// A user's effective role on a book (#80; C# BookRole/BookRoles). DTOs carry it as the wire string; parse it once.
export const bookRoles = ['view', 'comment', 'write', 'owner'] as const;
export type BookRole = (typeof bookRoles)[number];

/** The server only sends these four values; anything else is a contract violation. */
export function parseBookRole(value: string): BookRole {
  const found = bookRoles.find((role) => role === value);
  if (found === undefined) {
    throw new Error(
      `Unknown book role "${value}"; the server sends view, comment, write or owner.`,
    );
  }
  return found;
}

/** True when `role` is at least `minimum` (view < comment < write < owner). */
export function hasRole(role: BookRole, minimum: BookRole): boolean {
  return bookRoles.indexOf(role) >= bookRoles.indexOf(minimum);
}
