import { useBookHeader } from '~/book/queries';
import { accessOf, type SettingsAccess } from './access';

/** The caller's role from the header the book bar already loaded (one cache entry, no extra request). */
export function useSettingsAccess(bookId: string): SettingsAccess & { ownerName: string } {
  const { data } = useBookHeader(bookId);
  return { ...accessOf(data?.role), ownerName: data?.owner.displayName ?? '' };
}
