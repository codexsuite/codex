import { useCallback, useEffect, useState } from 'react';

// Whether Write's Preview pane is open, remembered in this browser for every book (`codex.write.preview`).
export const previewStorageKey = 'codex.write.preview';

function readOpen(): boolean {
  try {
    return window.localStorage.getItem(previewStorageKey) === 'open';
  } catch {
    return false;
  }
}

function writeOpen(open: boolean) {
  try {
    window.localStorage.setItem(previewStorageKey, open ? 'open' : 'closed');
  } catch {
    // The pane still opens for this visit; it just will not be remembered.
  }
}

/** Ctrl+Alt+P (⌘⌥P) toggles the pane from anywhere in Write, the prose included. */
export function isPreviewShortcut(event: KeyboardEvent): boolean {
  return (event.ctrlKey || event.metaKey) && event.altKey && event.key.toLowerCase() === 'p';
}

export function usePreviewPane(): [boolean, (open: boolean) => void] {
  const [open, setState] = useState(readOpen);
  const setOpen = useCallback((next: boolean) => {
    writeOpen(next);
    setState(next);
  }, []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (!isPreviewShortcut(event)) return;
      event.preventDefault();
      setState((was) => {
        writeOpen(!was);
        return !was;
      });
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  return [open, setOpen];
}
