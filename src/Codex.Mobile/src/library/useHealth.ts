import { getHealth, healthKey } from '@codex/core/api/health';
import { useQuery } from '@tanstack/react-query';

/** The server's health (and version). Disabled without an origin; a failure is data to ignore, not an error to show. */
export function useHealth(origin: string | null) {
  return useQuery({
    queryKey: healthKey(origin),
    queryFn: ({ signal }) => getHealth({ origin: origin ?? undefined, signal }),
    enabled: origin !== null,
    retry: false,
  });
}
