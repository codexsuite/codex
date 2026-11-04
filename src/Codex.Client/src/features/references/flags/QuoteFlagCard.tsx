import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiRequestError } from '@codex/core/api/client';
import { getQuoteFlags, notAQuote, otherSource, type QuoteFlag } from '@codex/core/api/quoteFlags';
import { manuscriptKey } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';
import { writeLink } from '@codex/core/shell/links';
import { citationsApi } from '../../write/citations';
import { refreshClearance } from '../refreshClearance';
import { refsTestIds } from '../testIds';

// One possible unflagged quote: where it is, why it was flagged, the quoted words and what to do with it.
export function QuoteFlagCard({
  bookId,
  flag,
  onResolved,
}: {
  bookId: string;
  flag: QuoteFlag;
  onResolved: (remaining: QuoteFlag[]) => void;
}) {
  const [asking, setAsking] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const resolve = async (write: () => Promise<QuoteFlag[]>) => {
    setBusy(true);
    setError(null);
    try {
      onResolved(await write());
    } catch (caught) {
      setError(
        caught instanceof ApiRequestError ? caught.message : 'The change could not be saved.',
      );
      setBusy(false);
    }
  };

  const where =
    flag.chapterNumber === null
      ? `Scene ${flag.sceneNumber}`
      : `Chapter ${flag.chapterNumber} · Scene ${flag.sceneNumber}`;
  const scene =
    flag.chapterNumber === null ? undefined : `${flag.chapterNumber}.${flag.sceneNumber}`;
  const citeLabel = flag.referenceLabel === null ? 'Cite a source…' : `Cite ${flag.referenceLabel}`;
  // Cite: the flagged words become a quote marker for the guessed source, in the scene's current prose. The scene's
  // prose, the markers and the clearance numbers all change, so they are read again.
  const cite = () =>
    resolve(async () => {
      const cited = await citationsApi.citeFlag(flag.id);
      await Promise.all([
        refreshClearance(queryClient, bookId),
        queryClient.invalidateQueries({ queryKey: manuscriptKey(bookId) }),
      ]);
      showToast({
        message: `Cited ${flag.referenceLabel ?? 'the source'} as reference ${cited.number}`,
      });
      return getQuoteFlags(bookId);
    });
  return (
    <div
      data-testid={refsTestIds.flagItem(flag.id)}
      className="flex flex-col gap-2 border-b border-border px-4 py-3"
    >
      <div className="flex items-baseline gap-2 text-sm">
        <span data-testid={refsTestIds.flagScene} className="font-medium text-muted">
          {where}
        </span>
        <span className="text-text">{flag.chapterTitle}</span>
      </div>
      <div data-testid={refsTestIds.flagWhy} className="text-sm text-warn">
        {flag.why}
      </div>
      <blockquote
        data-testid={refsTestIds.flagExcerpt}
        className="m-0 border-l-2 border-warn pl-3 text-base text-text"
      >
        {flag.excerpt}
      </blockquote>
      {error !== null && (
        <p role="alert" data-testid={refsTestIds.flagItemError} className="m-0 text-sm text-danger">
          {error}
        </p>
      )}
      {asking ? (
        <form
          data-testid={refsTestIds.flagOtherForm}
          className="flex flex-col gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void resolve(() => otherSource(flag.id, note));
          }}
        >
          <textarea
            aria-label="Where does this quote come from?"
            placeholder="Where does this quote come from?"
            value={note}
            data-testid={refsTestIds.flagOtherNote}
            onChange={(event) => setNote(event.target.value)}
            className="min-h-16 rounded-sm border border-border bg-surface p-2 text-sm text-text"
          />
          <div className="flex gap-2">
            <button
              type="submit"
              disabled={busy || note.trim() === ''}
              data-testid={refsTestIds.flagOtherSubmit}
              className="rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
            >
              Save
            </button>
            <button
              type="button"
              data-testid={refsTestIds.flagOtherCancel}
              onClick={() => setAsking(false)}
              className="rounded-sm border border-border px-3 py-1 text-sm text-text"
            >
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          {/* TODO: #23 W4.3 (J11): a flag with no guessed source gets a source picker here (citationsApi.citeFlag
              takes the source). Until then it is cited from Write. */}
          <button
            type="button"
            disabled={busy || flag.referenceId === null}
            title={
              flag.referenceId === null
                ? 'No source was matched. Select the words in Write and cite a source there.'
                : undefined
            }
            onClick={() => void cite()}
            data-testid={refsTestIds.flagCite}
            className="rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
          >
            {citeLabel}
          </button>
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.flagOther}
            onClick={() => setAsking(true)}
            className="rounded-sm border border-border px-3 py-1 text-sm text-text disabled:opacity-50"
          >
            Other source…
          </button>
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.flagNotQuote}
            onClick={() => void resolve(() => notAQuote(flag.id))}
            className="rounded-sm px-3 py-1 text-sm text-muted disabled:opacity-50"
          >
            Not a quote
          </button>
          <a
            data-testid={refsTestIds.flagWrite}
            href={writeLink(bookId, { scene })}
            className="text-sm text-accent"
          >
            Open in Write
          </a>
        </div>
      )}
    </div>
  );
}
