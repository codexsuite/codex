import { Link, useParams } from 'react-router-dom';
import { showToast } from '@codex/core/editing/toastStore';
import { planLink } from '@codex/core/shell/links';
import { ActionsMenu } from './ActionsMenu';
import type { ChapterView } from '@codex/core/features/write/manuscriptView';
import { writeTestIds } from './testIds';

interface ChapterHeaderProps {
  view: ChapterView;
  /** Narrows the manuscript to this chapter (the bookbar scope). */
  onShowOnly: (chapterId: string) => void;
}

async function copyReference(reference: string) {
  try {
    await navigator.clipboard.writeText(reference);
    showToast({ message: `Copied ${reference}. It stays valid after a reorder or rename.` });
  } catch {
    showToast({ message: `Could not copy ${reference}. Select it and copy by hand.` });
  }
}

// The chapter's number and title (with its opener art as a thumbnail when Plan has set one) beside its counts, its
// copyable `ch:slug` and an Actions menu (show only this chapter, copy the reference, open it in Plan).
export function ChapterHeader({ view, onShowOnly }: ChapterHeaderProps) {
  const { bookId = '' } = useParams();
  const { chapter } = view;
  const opener = chapter.opener;
  return (
    <header data-chapter-id={chapter.id} className="write-row write-chapter-header py-3 pb-4">
      <div className="min-w-0">
        {opener?.webUrl && (
          <img
            data-testid={writeTestIds.openerThumb(chapter.id)}
            src={opener.webUrl}
            alt=""
            title={opener.caption ? `Opener art: ${opener.caption}` : 'Opener art'}
            className="float-right ml-2 h-9 w-12 rounded-sm object-cover"
          />
        )}
        {view.overline && <div className="write-overline">{view.overline}</div>}
        <h2
          data-testid={writeTestIds.chapterTitle(chapter.id)}
          className="m-0 mt-0.5 text-lg leading-snug font-semibold"
        >
          {chapter.title}
        </h2>
      </div>
      <div className="write-gutter flex flex-wrap items-center gap-1 self-start pt-0.5 text-xs text-muted tabular-nums">
        <span data-testid={writeTestIds.chapterCounts(chapter.id)}>{view.countsLabel}</span>
        <span className="flex-1" />
        <button
          type="button"
          data-testid={writeTestIds.chapterCopyRef(chapter.id)}
          title="Copy a reference that survives reorders"
          onClick={() => void copyReference(view.reference)}
          className="write-mini"
        >
          <code className="font-mono text-2xs">{view.reference}</code>
        </button>
        <ActionsMenu
          label="Chapter actions"
          buttonTestId={writeTestIds.chapterMore(chapter.id)}
          menuTestId={writeTestIds.chapterMenu(chapter.id)}
        >
          {(close) => (
            <>
              <button
                type="button"
                role="menuitem"
                data-testid={writeTestIds.chapterScope(chapter.id)}
                className="write-menu-item"
                onClick={() => {
                  close();
                  onShowOnly(chapter.id);
                }}
              >
                Show only this chapter
              </button>
              <button
                type="button"
                role="menuitem"
                data-testid={writeTestIds.chapterMenuCopyRef(chapter.id)}
                className="write-menu-item"
                onClick={() => {
                  close();
                  void copyReference(view.reference);
                }}
              >
                Copy reference<span className="write-menu-key">{view.reference}</span>
              </button>
              {chapter.number !== null && (
                <Link
                  role="menuitem"
                  to={planLink(bookId, { chapter: String(chapter.number) })}
                  data-testid={writeTestIds.chapterOpenInPlan(chapter.id)}
                  className="write-menu-item"
                  onClick={close}
                >
                  Open in Plan<span className="write-menu-key">chapter={chapter.number}</span>
                </Link>
              )}
            </>
          )}
        </ActionsMenu>
      </div>
    </header>
  );
}
