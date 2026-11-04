import { useQuery } from '@tanstack/react-query';
import { getMe, meKey } from '@codex/core/api/auth';

/**
 * Who is signed in, fetched once per page load (sign-in, the account page and password changes update it in place).
 * A 401 never resolves here: the fetch wrapper sends the page to /login. Any other failure stays failed rather than
 * refetching for each new observer: AuthGate renders the page anyway, and a refetch would put it back to pending.
 */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: ({ signal }) => getMe(signal),
    retry: false,
    retryOnMount: false,
    staleTime: Infinity,
  });
}
