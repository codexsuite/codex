import { useMemo } from 'react';
import { useLocation } from 'react-router-dom';
import { parseDeepLink, type ParsedDeepLink } from '@codex/core/shell/deepLink';

/** The current URL as a typed object, or `{ ok: false, error }` when the link is malformed. Never throws. */
export function useDeepLink(): ParsedDeepLink {
  const { pathname, search, hash } = useLocation();
  return useMemo(() => parseDeepLink(pathname, search, hash), [pathname, search, hash]);
}
