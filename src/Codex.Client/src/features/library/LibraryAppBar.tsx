import type { ReactNode } from 'react';
import { PaletteButton } from '../../shell/PaletteButton';
import { UserMenu } from '../auth/UserMenu';

// design/screens/library.html app bar, shared by the Library and the Series screen: logo mark, the crumbs, ⌘K and the
// signed-in user's avatar menu.
export function LibraryAppBar({ crumbs }: { crumbs: ReactNode }) {
  return (
    <header className="flex h-[var(--bar-height)] items-center gap-3 border-b border-border bg-surface px-3">
      <span className="flex items-center gap-[7px] font-bold text-text">
        <i aria-hidden="true" className="size-4 rounded-[4px] bg-accent" />
        Codex
      </span>
      {crumbs}
      <PaletteButton />
      <UserMenu />
    </header>
  );
}
