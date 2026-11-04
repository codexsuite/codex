import { useMemo } from 'react';
import { useAdditions } from '~/book/queries';
import { additionCounts } from './planModel';

/** Additions per scene for the cards' "+2 additions". A failed read just shows no count. */
export function useAdditionCounts(bookId: string): Map<string, number> {
  const additions = useAdditions(bookId);
  return useMemo(() => additionCounts(additions.data?.additions ?? []), [additions.data]);
}
