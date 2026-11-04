import { useEffect, useState } from 'react';
import { coverPalettes, type CoverPalette } from '@codex/core/api/library';
import { libraryTestIds } from './testIds';

// A book cover is the book's artwork: it renders the same in both themes, so its colours are not UI tokens.
const covers = {
  'cover-1': ['#05070d', '#0d1a2b', '#22d3ee'],
  'cover-2': ['#d9cfc0', '#b9ab97', '#5b4f43'],
  'cover-3': ['#2a1d12', '#5c3b1e', '#f3d7a4'],
  'cover-4': ['#1b2320', '#3d4f45', '#c9e4d5'],
  'cover-5': ['#3b0d1f', '#9d174d', '#fbcfe8'],
  'cover-6': ['#1d1d2b', '#45306b', '#d9c7ff'],
  'cover-7': ['#08263a', '#155e75', '#bae6fd'],
  'cover-8': ['#3a1406', '#a3410f', '#ffe1c2'],
} as const;

interface CoverProps {
  palette: string;
  title: string;
  /** The author as printed, drawn at the foot; null or omitted draws none. */
  author?: string | null;
  /** The uploaded cover (`coverUrl`); null or missing shows the generated cover. */
  imageUrl?: string | null;
  className?: string;
  testId?: string;
}

const frame = 'aspect-[2/3] w-[var(--cover-width)] overflow-hidden rounded-xs';
const shadow = 'shadow-[0_1px_2px_rgb(0_0_0/.25),0_0_0_1px_rgb(0_0_0/.08)]';

export function Cover({
  palette,
  title,
  author = null,
  imageUrl = null,
  className = '',
  testId = libraryTestIds.cover,
}: CoverProps) {
  const known = coverPalettes.find((name): name is CoverPalette => name === palette);
  // An image that will not load (its file gone) falls back to the generated cover rather than a broken image.
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  useEffect(() => {
    if (known === undefined) {
      console.error(`Unknown cover palette "${palette}"; the server returns cover-1 to cover-8.`);
    }
  }, [known, palette]);
  if (known === undefined) {
    // A contract violation stays visible here instead of crashing the screen that shows the cover.
    return (
      <div
        data-testid={libraryTestIds.unknownCover}
        title={`Unknown cover palette "${palette}"`}
        className={`flex items-center justify-center border-2 border-dashed border-danger text-danger ${frame} ${className}`}
      >
        ?
      </div>
    );
  }
  if (imageUrl !== null && imageUrl !== failedUrl) {
    return (
      <img
        data-testid={testId}
        src={imageUrl}
        alt={title}
        onError={() => setFailedUrl(imageUrl)}
        className={`block object-cover ${frame} ${shadow} ${className}`}
      />
    );
  }
  const [from, to, ink] = covers[known];
  return (
    <div
      data-testid={testId}
      className={`flex flex-col justify-between p-[9%_8%] ${frame} ${shadow} ${className}`}
      style={{ background: `linear-gradient(160deg, ${from}, ${to})`, color: ink }}
    >
      <span className="text-[calc(var(--cover-width)*.1)] leading-[1.05] font-bold tracking-[.02em] uppercase">
        {title}
      </span>
      {author && (
        <span
          data-testid={libraryTestIds.coverAuthor}
          className="truncate text-center text-[calc(var(--cover-width)*.065)] opacity-80"
        >
          {author}
        </span>
      )}
    </div>
  );
}
