import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useModalFocus } from '../editing/useModalFocus';
import { PaletteContent } from '../features/library/PaletteContent';
import { testIds } from '../testIds';
import { Slot } from './Slots';

// The command palette frame: the global shortcut, the dimmed overlay, and its content (the Library lane's
// PaletteContent: books, series and actions; the one shell -> feature import). The `palette` slot stays for extras.
// Esc or a click on the backdrop closes it and focus returns to what had it.

interface PaletteControl {
  open: boolean;
  setOpen: (open: boolean) => void;
}

const PaletteContext = createContext<PaletteControl | null>(null);

export function usePalette(): PaletteControl {
  const control = useContext(PaletteContext);
  if (control === null) throw new Error('usePalette needs the app shell.');
  return control;
}

export function PaletteProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const value = useMemo(() => ({ open, setOpen }), [open]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  return (
    <PaletteContext.Provider value={value}>
      {children}
      <PaletteOverlay />
    </PaletteContext.Provider>
  );
}

function PaletteOverlay() {
  const { open, setOpen } = usePalette();
  const close = useCallback(() => setOpen(false), [setOpen]);
  return open ? <PaletteDialog close={close} /> : null;
}

function PaletteDialog({ close }: { close: () => void }) {
  const dialog = useRef<HTMLDivElement>(null);

  // The content's search field gets focus when it has one, else the frame. Before the hook, which moves focus to the
  // frame only when nothing inside has it, and gives it back to what had it when the palette closes.
  useEffect(() => {
    (dialog.current?.querySelector('input') ?? dialog.current)?.focus();
  }, []);
  useModalFocus(dialog);

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 pt-[12vh]"
      onMouseDown={(event) => event.target === event.currentTarget && close()}
    >
      <div
        ref={dialog}
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        tabIndex={-1}
        data-testid={testIds.shell.commandPalette}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.stopPropagation();
            close();
          }
        }}
        className="max-h-[70vh] w-[min(560px,92vw)] overflow-auto rounded-lg border border-border bg-surface shadow-raised outline-none"
      >
        <PaletteContent />
        <Slot name="palette" />
      </div>
    </div>
  );
}
