import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { deviceSessionsKey, listDeviceSessions, revokeDeviceSession } from '@codex/core/api/auth';
import { ApiRequestError } from '@codex/core/api/client';
import { authTestIds } from './testIds';

// Account → Signed-in devices (#35): the phones signed in to the Codex app with a device session. Sign out revokes one;
// that device's secret stops working at once.
export function DeviceSessionsSection() {
  const queryClient = useQueryClient();
  const sessions = useQuery({
    queryKey: deviceSessionsKey,
    queryFn: ({ signal }) => listDeviceSessions(signal),
  });
  const [error, setError] = useState<string | null>(null);

  async function signOut(id: string) {
    setError(null);
    try {
      await revokeDeviceSession(id);
    } catch (e) {
      setError(
        e instanceof ApiRequestError ? e.message : 'Could not sign the device out. Try again.',
      );
    }
    await queryClient.invalidateQueries({ queryKey: deviceSessionsKey });
  }

  return (
    <section
      data-testid={authTestIds.devicesSection}
      className="flex flex-col gap-3 rounded-lg border border-border bg-surface p-4"
    >
      <h2 className="m-0 text-md font-semibold">Signed-in devices</h2>
      <p className="m-0 text-sm text-muted">
        Phones signed in to the Codex app with your account. Signing a device out ends its session
        at once.
      </p>
      {error !== null && (
        <p role="alert" data-testid={authTestIds.devicesError} className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
      {sessions.data?.length === 0 && (
        <p data-testid={authTestIds.devicesEmpty} className="m-0 text-sm text-muted">
          No devices are signed in. Sign in from the Codex app on your phone with this server&apos;s
          address.
        </p>
      )}
      {sessions.data !== undefined && sessions.data.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {sessions.data.map((session) => (
            <li
              key={session.id}
              data-testid={authTestIds.deviceRow(session.id)}
              className="flex items-center gap-3 border-b border-border pb-2"
            >
              <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="text-sm text-text">
                  {session.name}{' '}
                  <span className="font-mono text-xs text-muted">{session.prefix}…</span>
                </span>
                <span className="text-xs text-muted">
                  Signed in {session.createdLabel}
                  {' · '}
                  {session.lastUsedLabel === null
                    ? 'Not used yet'
                    : `Last used ${session.lastUsedLabel}`}
                  {' · '}
                  Expires {session.expiresLabel}
                </span>
              </div>
              <button
                type="button"
                data-testid={authTestIds.deviceSignOut(session.id)}
                className="h-7 rounded border border-border bg-transparent px-2 text-xs text-text"
                onClick={() => void signOut(session.id)}
              >
                Sign out
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
