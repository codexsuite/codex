import { usePalette } from './CommandPalette';
import { shellTestIds } from './shellTestIds';

// The ⌘K key cap in the bars: same as pressing the shortcut.
export function PaletteButton() {
  const { setOpen } = usePalette();
  return (
    <button
      type="button"
      data-testid={shellTestIds.paletteButton}
      aria-label="Open command palette"
      title="Command palette (⌘K / Ctrl+K)"
      onClick={() => setOpen(true)}
      className="cursor-pointer rounded-[4px] border border-border bg-surface-2 px-[5px] py-px text-xs text-muted hover:text-text focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent"
    >
      ⌘K
    </button>
  );
}
