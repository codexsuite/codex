import { Link } from 'react-router-dom';
import { PaletteButton } from '../../shell/PaletteButton';
import { libraryLink } from '@codex/core/shell/links';
import { UserMenu } from './UserMenu';

// The bar over the account and user pages: back to the Library, the page title, then the bar's usual right side.
export function AuthPageBar({ title }: { title: string }) {
  return (
    <header className="flex h-[var(--bar-height)] items-center gap-2 border-b border-border px-4">
      <Link to={libraryLink()} className="text-md font-semibold text-text no-underline">
        Codex
      </Link>
      <span className="text-muted">/</span>
      <h1 className="m-0 text-md font-semibold text-text">{title}</h1>
      <span className="flex-1" />
      <PaletteButton />
      <UserMenu />
    </header>
  );
}
