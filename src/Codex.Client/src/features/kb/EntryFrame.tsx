import { useEffect, useRef, type ReactNode } from 'react';
import { useModalFocus } from '../../editing/useModalFocus';
import { kbTestIds } from './testIds';

interface EntryFrameProps {
  label: string;
  /** `category-*` token for the 3px bar along the top, or null. */
  colour: string | null;
  expanded: boolean;
  onClose: () => void;
  children: ReactNode;
}

// The window itself: a scrim over the main area (the sidebar stays usable), the dialog filling it down to a --space-4
// margin, and the category-colour bar. Esc closes it, Tab stays inside it and focus goes back to what opened it; Expand fills the main area edge to edge.
export function EntryFrame({ label, colour, expanded, onClose, children }: EntryFrameProps) {
  const dialog = useRef<HTMLDivElement>(null);
  useModalFocus(dialog);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className={`fixed top-[var(--bar-height)] right-0 bottom-0 left-[var(--sidebar-width)] z-20 flex justify-center bg-[color-mix(in_srgb,var(--color-bg)_62%,transparent)] max-[720px]:left-0 max-[720px]:p-0 ${
        expanded ? 'p-0' : 'p-[var(--space-4)]'
      }`}
    >
      <div
        ref={dialog}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        tabIndex={-1}
        data-testid={kbTestIds.entryWindow}
        data-expanded={expanded}
        className={`@container relative flex outline-none min-h-0 flex-col overflow-hidden bg-surface max-[720px]:h-full max-[720px]:w-full max-[720px]:rounded-none max-[720px]:border-0 max-[720px]:shadow-none ${
          expanded
            ? 'h-full w-full'
            : 'w-[min(var(--overlay-width),100%)] rounded-lg border border-border [box-shadow:var(--shadow-raised),0_24px_56px_-24px_rgb(0_0_0/.45)]'
        }`}
        style={{ ['--cat' as string]: colour ? `var(--${colour})` : 'var(--color-border)' }}
      >
        {!expanded && <div className="h-[3px] shrink-0 bg-[var(--cat)]" />}
        {children}
      </div>
    </div>
  );
}
