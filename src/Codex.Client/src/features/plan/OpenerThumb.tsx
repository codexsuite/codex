import type { OutlineOpener } from '@codex/core/api/structure';
import { ImageIcon } from './icons';

// The act's or chapter's opener art as a small thumbnail in its header; a click opens Opener art… to change it.
// Until the web rendition exists it shows an image glyph.
export function OpenerThumb({
  opener,
  name,
  testId,
  onClick,
}: {
  opener: OutlineOpener | null;
  name: string;
  testId: string;
  /** Opens Opener art…; without it (below write, #80) the thumbnail only shows. */
  onClick?: () => void;
}) {
  if (opener === null) return null;
  const title = opener.caption ? `Opener art: ${opener.caption}` : 'Opener art';
  return (
    <button
      type="button"
      data-testid={testId}
      title={title}
      aria-label={`${title} · ${name}`}
      onClick={onClick}
      disabled={onClick === undefined}
      className="inline-grid h-[22px] w-[30px] flex-none cursor-pointer place-items-center overflow-hidden rounded-xs border border-border bg-surface-2 p-0 text-muted hover:border-accent focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-[13px]"
    >
      {opener.webUrl !== null ? (
        <img src={opener.webUrl} alt="" className="size-full object-cover" />
      ) : (
        <ImageIcon />
      )}
    </button>
  );
}
