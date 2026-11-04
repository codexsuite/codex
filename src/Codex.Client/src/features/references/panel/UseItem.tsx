import { useState, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import type { SourceUse, UseKind } from '@codex/core/api/referenceSources';
import { showToast } from '@codex/core/editing/toastStore';
import { writeLink } from '@codex/core/shell/links';
import { writeReturnLink } from '../../write/citeReturn';
import { RefIcon } from '../RefIcon';
import { refsTestIds } from '../testIds';
import { sceneOf, whereOf } from './paraphraseTarget';

export const useKinds: { code: UseKind; label: string }[] = [
  { code: 'quote', label: 'Quote' },
  { code: 'paraphrase', label: 'Paraphrase' },
  { code: 'data', label: 'Data' },
  { code: 'figure', label: 'Figure' },
  { code: 'lyric', label: 'Lyric' },
];

// The colour each use type highlights its words in (design/screens/references.html `USE`).
const useTones: Record<UseKind, string> = {
  quote: 'var(--color-info)',
  paraphrase: 'var(--color-muted)',
  data: 'var(--color-info)',
  figure: 'var(--color-warn)',
  lyric: 'var(--color-danger)',
};

// `.mini` of the reference screens.
const mini =
  'inline-flex h-[22px] cursor-pointer items-center gap-1 rounded-xs border-0 bg-transparent px-1.5 text-xs font-medium whitespace-nowrap text-muted no-underline hover:bg-surface-2 hover:text-text';

// One passage that cites the source: where it is, the used words highlighted in the use type's colour (a paraphrase
// gets a dotted underline instead) with the source's marker after them, how it uses the source, and a way into Write.
export function UseItem({
  bookId,
  sourceId,
  use,
  marker,
  busy,
  onKind,
}: {
  bookId: string;
  sourceId: string;
  use: SourceUse;
  /** The source's marker number and status colour, as Write shows them. */
  marker: { number: number | null; tone: string };
  busy: boolean;
  onKind: (kind: UseKind) => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const scene = sceneOf(use);
  const kindTone = { '--tone': useTones[use.kind] } as CSSProperties;
  const copy = () => {
    setMenuOpen(false);
    navigator.clipboard.writeText(use.excerpt).then(
      () => showToast({ message: 'Passage copied' }),
      () => showToast({ message: 'The passage could not be copied' }),
    );
  };
  return (
    <div
      data-testid={refsTestIds.use(use.id)}
      className="border-b border-dashed border-border py-2 first:pt-0 last:border-b-0 last:pb-0"
    >
      <div className="flex min-h-[22px] items-center gap-1.5">
        <span className="text-2xs font-medium tracking-[.02em] whitespace-nowrap text-muted uppercase">
          {whereOf(use)}
        </span>
        <span className="min-w-0 flex-1 truncate text-xs text-muted">{use.chapterTitle}</span>
        {use.newSinceSignOff && (
          <span
            data-testid={refsTestIds.useNew(use.id)}
            className="inline-flex h-4 items-center rounded-lg bg-[color-mix(in_srgb,var(--color-warn)_16%,transparent)] px-[5px] text-2xs font-semibold text-warn"
          >
            New since cleared
          </span>
        )}
        {use.pending && <span className="text-xs text-muted">In a draft</span>}
        <span
          style={kindTone}
          className="relative inline-flex h-[18px] items-center rounded-[9px] bg-[color-mix(in_srgb,var(--tone)_14%,transparent)] pr-1.5 pl-1.5 text-2xs font-medium whitespace-nowrap text-text"
        >
          <i aria-hidden className="mr-1 size-1.5 rounded-full bg-[var(--tone)]" />
          <select
            aria-label="How this passage uses the source"
            title="How this passage uses the source"
            data-testid={refsTestIds.useKind(use.id)}
            value={use.kind}
            disabled={busy}
            onChange={(event) => onKind(event.target.value as UseKind)}
            className="cursor-pointer appearance-none border-0 bg-transparent p-0 text-2xs font-medium text-text outline-0 field-sizing-content"
          >
            {useKinds.map((kind) => (
              <option key={kind.code} value={kind.code}>
                {kind.label}
              </option>
            ))}
          </select>
        </span>
      </div>
      <div
        style={kindTone}
        className="my-1 border-l-2 border-border px-2.5 py-1.5 text-sm leading-[var(--leading-prose)] text-muted"
      >
        <q
          data-testid={refsTestIds.useExcerpt(use.id)}
          className={`rounded-[2px] px-px text-text [quotes:none] ${
            use.kind === 'paraphrase'
              ? 'underline decoration-[color-mix(in_srgb,var(--color-muted)_70%,transparent)] decoration-dotted underline-offset-[3px]'
              : 'bg-[color-mix(in_srgb,var(--tone)_14%,transparent)]'
          }`}
        >
          {use.excerpt}
        </q>
        {marker.number !== null && (
          <sup
            style={{ '--tone': marker.tone } as CSSProperties}
            className="ml-px inline-block min-w-[14px] rounded-[3px] bg-[color-mix(in_srgb,var(--tone)_16%,transparent)] px-[3px] text-center align-super text-2xs leading-[1.3] font-semibold text-[var(--tone)] tabular-nums"
          >
            {marker.number}
          </sup>
        )}
      </div>
      <div className="-ml-1.5 flex flex-wrap items-center gap-0.5">
        <Link
          to={writeLink(bookId, { scene, ref: sourceId })}
          data-testid={refsTestIds.useWrite(use.id)}
          title={`Opens ${whereOf(use)} in Write at this marker`}
          className={mini}
        >
          <RefIcon name="ext" className="size-[11px]" />
          Open in Write
        </Link>
        {use.kind === 'quote' && !use.pending && (
          <Link
            to={writeReturnLink(bookId, { scene, ref: sourceId, paraphrase: true })}
            data-testid={refsTestIds.useParaphrase(use.id)}
            title="Opens this passage in Write with a Scene beat that rewrites it in your words"
            className={mini}
          >
            <RefIcon name="spark" className="size-[11px]" />
            Paraphrase with AI
          </Link>
        )}
        <div className="relative">
          <button
            type="button"
            aria-label="More actions"
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen(!menuOpen)}
            className="inline-grid size-[22px] cursor-pointer place-items-center rounded-xs border-0 bg-transparent p-0 text-muted hover:bg-surface-2 hover:text-text"
          >
            <RefIcon name="more" />
          </button>
          {menuOpen && (
            <div
              role="menu"
              className="absolute left-0 z-10 mt-1 flex w-40 flex-col rounded-md border border-border bg-surface p-1 shadow-raised"
            >
              <button
                type="button"
                role="menuitem"
                onClick={copy}
                className="flex min-h-7 cursor-pointer items-center rounded-sm border-0 bg-transparent px-2 text-left text-sm text-text hover:bg-accent-soft"
              >
                Copy passage
              </button>
            </div>
          )}
        </div>
        <span className="ml-auto text-2xs text-muted tabular-nums">
          {[use.words > 0 ? `${use.words} words` : null, use.locator].filter(Boolean).join(' · ')}
        </span>
      </div>
    </div>
  );
}
