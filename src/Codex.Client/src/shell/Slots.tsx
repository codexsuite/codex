import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { testIds } from '../testIds';

// The slot API: shell-owned places in the book bar (and the command palette) that a lane page fills without
// editing a shell file. A <Slot> marks the place; a <SlotFill> anywhere in the tree portals its children into it.
// Nothing is rendered when the slot is not on screen (e.g. a fill on a page outside the book workspace).

export const slotNames = ['badge', 'scope', 'format', 'focus', 'saveStatus', 'palette'] as const;
export type SlotName = (typeof slotNames)[number];

export const slotTestIds: Record<SlotName, string> = {
  badge: testIds.shell.badgeSlot,
  scope: testIds.shell.scopeSlot,
  format: testIds.shell.formatSlot,
  focus: testIds.shell.focusSlot,
  saveStatus: testIds.shell.saveStatusSlot,
  palette: testIds.shell.paletteSlot,
};

interface SlotRegistry {
  targets: Partial<Record<SlotName, HTMLElement>>;
  register: (name: SlotName, element: HTMLElement | null) => void;
}

const SlotContext = createContext<SlotRegistry | null>(null);

export function SlotsProvider({ children }: { children: ReactNode }) {
  const [targets, setTargets] = useState<SlotRegistry['targets']>({});
  const register = useCallback((name: SlotName, element: HTMLElement | null) => {
    setTargets((current) => {
      if ((current[name] ?? null) === element) return current;
      const next = { ...current };
      if (element === null) delete next[name];
      else next[name] = element;
      return next;
    });
  }, []);
  const value = useMemo(() => ({ targets, register }), [targets, register]);
  return <SlotContext.Provider value={value}>{children}</SlotContext.Provider>;
}

function useRegistry(): SlotRegistry {
  const registry = useContext(SlotContext);
  if (registry === null)
    throw new Error('Slots need a <SlotsProvider> (the app shell mounts one).');
  return registry;
}

/** A place a lane can fill. `className` styles the wrapper the fill is portalled into. */
export function Slot({ name, className }: { name: SlotName; className?: string }) {
  const { register } = useRegistry();
  const ref = useCallback(
    (element: HTMLElement | null) => register(name, element),
    [register, name],
  );
  return <div ref={ref} data-testid={slotTestIds[name]} className={className ?? 'contents'} />;
}

/** Renders `children` into the named slot, wherever the fill sits in the tree. */
export function SlotFill({ slot, children }: { slot: SlotName; children: ReactNode }) {
  const { targets } = useRegistry();
  const target = targets[slot];
  return target === undefined ? null : createPortal(children, target);
}
