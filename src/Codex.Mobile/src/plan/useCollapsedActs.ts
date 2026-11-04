import { useCallback, useState } from 'react';
import { isActCollapsed, readCollapsed, writeCollapsed } from './collapsedActs';

/** Whether each act of the book is collapsed, remembered per book on the device. */
export function useCollapsedActs(bookId: string) {
  const [stored, setStored] = useState(() => readCollapsed(bookId));
  const isCollapsed = useCallback(
    (actId: string, actIndex: number) => isActCollapsed(stored, actId, actIndex),
    [stored],
  );
  const toggle = useCallback(
    (actId: string, actIndex: number) =>
      setStored((current) => {
        const next = { ...current, [actId]: !isActCollapsed(current, actId, actIndex) };
        writeCollapsed(bookId, next);
        return next;
      }),
    [bookId],
  );
  return { isCollapsed, toggle };
}
