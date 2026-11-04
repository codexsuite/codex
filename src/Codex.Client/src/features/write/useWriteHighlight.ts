import { useHighlight } from '../../shell/useHighlight';
import { useDeepLink } from '../../shell/useDeepLink';

// The highlighted Knowledge base entry: `#kb=` wins over the one stored for the book (the sidebar sets both).
export function useWriteHighlight(bookId: string) {
  const deepLink = useDeepLink();
  return useHighlight(bookId, deepLink.ok ? deepLink.link.kb : undefined);
}
