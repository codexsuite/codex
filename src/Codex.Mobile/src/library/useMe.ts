import { getMe, meKey } from '@codex/core/api/auth';
import { useQuery } from '@tanstack/react-query';

/** `GET /api/auth/me`: the signed-in user. Sign-in seeds this cache entry, so it is usually there already. */
export function useMe() {
  return useQuery({ queryKey: meKey, queryFn: ({ signal }) => getMe(signal) });
}
