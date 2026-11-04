import { ApiRequestError } from '../api/client';

// A save refused as `forbidden` (403, #80) means this user's role on the book dropped, maybe before the AccessChanged
// event arrived. Whoever sees one reports it here; the book workspace listens and re-reads the header, so the screen
// turns read-only instead of staying editable.
const listeners = new Set<() => void>();

/** True (and the workspace told) when `error` is a 403 `forbidden`. */
export function reportForbidden(error: unknown): boolean {
  if (!isForbidden(error)) return false;
  listeners.forEach((listener) => listener());
  return true;
}

export function isForbidden(error: unknown): error is ApiRequestError {
  return error instanceof ApiRequestError && error.status === 403;
}

export function onForbidden(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
