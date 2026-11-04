import { useLocation, useNavigate } from 'react-router-dom';
import { withHashKey } from './hashParams';

// Opens and closes the entry window by editing the URL hash (`#entry=`), keeping every other key.
export function useEntryHash() {
  const navigate = useNavigate();
  const { pathname, search, hash } = useLocation();
  const go = (next: string) => void navigate({ pathname, search, hash: next }, { replace: true });
  return {
    open: (entryId: string) => go(withHashKey(withHashKey(hash, 'kb', entryId), 'entry', entryId)),
    close: () => go(withHashKey(hash, 'entry', null)),
  };
}
