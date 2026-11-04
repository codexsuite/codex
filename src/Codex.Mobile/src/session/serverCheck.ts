// The sign-in screen's server health check (#35): what state it is in and what to show. Pure: no Expo or React imports,
// so vitest loads it. `useServerCheck.ts` runs the check; `getHealth` is passed in.

import { displayVersion } from './displayVersion';
import { normalizeServerAddress, serverHost } from './serverAddress';

export type ServerCheck =
  | { kind: 'idle' }
  | { kind: 'checking'; origin: string }
  | { kind: 'ok'; origin: string; version: string }
  | { kind: 'failed'; message: string };

export const IDLE_CHECK: ServerCheck = { kind: 'idle' };

export type HealthFetcher = (options: { origin: string; signal: AbortSignal }) => Promise<{
  status?: unknown;
  version?: unknown;
}>;

export function notCodexMessage(host: string): string {
  return `${host} isn't a Codex server. Check the address.`;
}

export function unreachableMessage(host: string): string {
  return `Can't reach ${host}. Check the address and that the server is running.`;
}

/**
 * Checks one address: a bad address fails with the address message, a server that answers is `ok` with its version,
 * anything else (no answer, an error status) is `failed` naming the host. A check cut off by `signal` returns `null`:
 * the address changed, so nobody wants the answer.
 */
export async function runServerCheck(
  server: string,
  fetchHealth: HealthFetcher,
  signal: AbortSignal,
  onChecking: (origin: string) => void = () => undefined,
): Promise<ServerCheck | null> {
  const address = normalizeServerAddress(server);
  if (!address.ok) return { kind: 'failed', message: address.error };

  onChecking(address.origin);
  try {
    const health = await fetchHealth({ origin: address.origin, signal });
    if (signal.aborted) return null;
    // Another service's /api/health can answer 200 too: only Codex's `{ status: 'ok', version }` counts.
    if (health.status !== 'ok' || typeof health.version !== 'string') {
      return { kind: 'failed', message: notCodexMessage(serverHost(address.origin)) };
    }
    return { kind: 'ok', origin: address.origin, version: health.version };
  } catch {
    if (signal.aborted) return null;
    return { kind: 'failed', message: unreachableMessage(serverHost(address.origin)) };
  }
}

/** Sign in is allowed only when the check passed for the address as it stands now. */
export function canSignIn(check: ServerCheck, server: string): boolean {
  if (check.kind !== 'ok') return false;
  const address = normalizeServerAddress(server);
  return address.ok && address.origin === check.origin;
}

/** The line under the Server field, or `null` when there is nothing to say. */
export function checkLine(
  check: ServerCheck,
): { tone: 'muted' | 'ok' | 'danger'; text: string } | null {
  switch (check.kind) {
    case 'idle':
      return null;
    case 'checking':
      return { tone: 'muted', text: `Checking ${serverHost(check.origin)}…` };
    case 'ok':
      return { tone: 'ok', text: `Codex ${displayVersion(check.version)} · connected` };
    case 'failed':
      return { tone: 'danger', text: check.message };
  }
}
