import { createContext } from 'react';
import type { Arrow, MoveKind } from '@codex/core/features/plan/moveOutline';

/** What a drag grip asks of the board: take one Alt+arrow step. */
export interface Reorder {
  keyMove: (kind: MoveKind, id: string, key: Arrow) => void;
}

export const ReorderContext = createContext<Reorder>({ keyMove: () => undefined });
