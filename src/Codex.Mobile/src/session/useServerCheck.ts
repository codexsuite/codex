import { useCallback, useEffect, useRef, useState } from 'react';
import { getHealth } from '@codex/core/api/health';
import {
  IDLE_CHECK,
  canSignIn,
  runServerCheck,
  type HealthFetcher,
  type ServerCheck,
} from './serverCheck';

/**
 * The sign-in screen's health check. `check` is what to show; `verify` runs a check for the address (a stale one is
 * aborted) and says whether Sign in may go ahead; `reset` forgets the check and aborts one in flight, for when the
 * address is edited.
 */
export function useServerCheck(
  server: string,
  options: { checkOnMount?: boolean; fetchHealth?: HealthFetcher } = {},
) {
  const { checkOnMount = false, fetchHealth = getHealth } = options;
  const [check, setCheck] = useState<ServerCheck>(IDLE_CHECK);
  const inFlight = useRef<AbortController | null>(null);
  const latestServer = useRef(server);
  latestServer.current = server;

  const reset = useCallback(() => {
    inFlight.current?.abort();
    inFlight.current = null;
    setCheck(IDLE_CHECK);
  }, []);

  const verify = useCallback(
    async (address: string): Promise<boolean> => {
      inFlight.current?.abort();
      const controller = new AbortController();
      inFlight.current = controller;
      const result = await runServerCheck(address, fetchHealth, controller.signal, (origin) =>
        setCheck({ kind: 'checking', origin }),
      );
      if (result === null) return false;
      inFlight.current = null;
      setCheck(result);
      return canSignIn(result, address);
    },
    [fetchHealth],
  );

  useEffect(() => {
    if (checkOnMount && latestServer.current.trim() !== '') void verify(latestServer.current);
    return () => inFlight.current?.abort();
  }, [checkOnMount, verify]);

  return { check, verify, reset };
}
