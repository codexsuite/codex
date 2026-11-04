import { useEffect, useRef, useState, type ReactNode } from 'react';
import { MoreIcon } from './icons';

interface ActionsMenuProps {
  label: string;
  buttonTestId: string;
  menuTestId: string;
  /** The menu's items; `close` shuts the menu (call it before acting). */
  children: (close: () => void) => ReactNode;
}

// A ⋮ icon button and its menu (design/screens/write.html `#pop` menus). A click outside or Esc closes it.
export function ActionsMenu({ label, buttonTestId, menuTestId, children }: ActionsMenuProps) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    if (!open) return;
    const dismiss = (event: Event) => {
      if (
        event instanceof KeyboardEvent
          ? event.key === 'Escape'
          : !root.current?.contains(event.target as Node)
      ) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', dismiss);
    return () => {
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', dismiss);
    };
  }, [open]);
  return (
    <span ref={root} className="relative inline-flex">
      <button
        type="button"
        data-testid={buttonTestId}
        aria-label={label}
        title={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className="write-ib"
      >
        <MoreIcon />
      </button>
      {open && (
        <div role="menu" data-testid={menuTestId} className="write-menu top-full right-0 mt-1">
          {children(() => setOpen(false))}
        </div>
      )}
    </span>
  );
}
