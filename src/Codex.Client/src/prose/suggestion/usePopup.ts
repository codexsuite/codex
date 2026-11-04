import { useMemo, useRef, useState } from 'react';
import type { SuggestionKeyDownProps, SuggestionProps } from '@tiptap/suggestion';

export interface PopupView<T> {
  items: T[];
  index: number;
  /** Caret rectangle in viewport coordinates, or null when the editor cannot say. */
  rect: DOMRect | null;
  select: (item: T) => void;
}

export interface PopupController<T> {
  onStart: (props: SuggestionProps<T, T>) => void;
  onUpdate: (props: SuggestionProps<T, T>) => void;
  onExit: () => void;
  onKeyDown: (props: SuggestionKeyDownProps) => boolean;
}

/** Holds the state of one suggestion popup and the callbacks a TipTap suggestion plugin drives it with. */
export function usePopup<T>(): { view: PopupView<T> | null; controller: PopupController<T> } {
  const [view, setView] = useState<PopupView<T> | null>(null);
  const live = useRef<PopupView<T> | null>(null);

  const controller = useMemo<PopupController<T>>(() => {
    const show = (props: SuggestionProps<T, T>, index: number) => {
      const next: PopupView<T> = {
        items: props.items,
        index: Math.min(index, Math.max(0, props.items.length - 1)),
        rect: props.clientRect?.() ?? null,
        select: (item) => props.command(item),
      };
      live.current = next;
      setView(next);
    };
    return {
      onStart: (props) => show(props, 0),
      onUpdate: (props) => show(props, live.current?.index ?? 0),
      onExit: () => {
        live.current = null;
        setView(null);
      },
      onKeyDown: ({ event }) => {
        const current = live.current;
        if (!current || current.items.length === 0) return false;
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
          const step = event.key === 'ArrowDown' ? 1 : -1;
          const next = {
            ...current,
            index: (current.index + step + current.items.length) % current.items.length,
          };
          live.current = next;
          setView(next);
          return true;
        }
        if (event.key === 'Enter' || event.key === 'Tab') {
          current.select(current.items[current.index]);
          return true;
        }
        return false;
      },
    };
  }, []);

  return { view, controller };
}
