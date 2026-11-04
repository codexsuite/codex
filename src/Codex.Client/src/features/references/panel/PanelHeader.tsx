import { useState } from 'react';
import type { SourceDetail } from '@codex/core/api/referenceSources';
import { RefIcon } from '../RefIcon';
import { refsTestIds } from '../testIds';
import { useCanWrite } from '../../../shell/bookAccess';

// `.ib` of the reference screens.
const iconButton =
  'inline-grid size-[22px] shrink-0 cursor-pointer place-items-center rounded-xs border-0 bg-transparent p-0 text-muted hover:bg-surface-2 hover:text-text';

// Reference number and kind, the source actions menu, title, byline, and the button that closes the panel.
export function PanelHeader({
  source,
  onClose,
  onRemove,
  onMerge,
}: {
  source: SourceDetail;
  onClose: () => void;
  onRemove: () => void;
  onMerge: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  // Below write (#80) the menu only opens the source; no merge or remove.
  const canWrite = useCanWrite();
  const byline = [source.authorsLabel, source.year, source.publisher]
    .filter((part) => part !== '' && part !== null)
    .join(' · ');
  const url = source.details.url;
  return (
    <header className="sticky top-0 z-[3] border-b border-border bg-surface px-3 pt-3 pb-2">
      <div className="flex items-center gap-1.5">
        <span className="min-w-0 flex-1 truncate text-2xs font-medium tracking-[.02em] text-muted uppercase">
          {source.number === null ? 'Not cited' : `Reference ${source.number}`} · {source.kindLabel}
        </span>
        {(canWrite || url !== null) && (
          <div className="relative">
            <button
              type="button"
              aria-label="Source actions"
              aria-haspopup="menu"
              aria-expanded={menuOpen}
              data-testid={refsTestIds.panelMenu}
              onClick={() => setMenuOpen(!menuOpen)}
              className={iconButton}
            >
              <RefIcon name="more" />
            </button>
            {menuOpen && (
              <div
                role="menu"
                className="absolute right-0 z-10 mt-1 flex w-[236px] flex-col rounded-md border border-border bg-surface p-1 shadow-raised"
              >
                {url !== null && (
                  <a
                    role="menuitem"
                    href={url}
                    target="_blank"
                    rel="noreferrer"
                    data-testid={refsTestIds.panelMenuOpen}
                    onClick={() => setMenuOpen(false)}
                    className="flex min-h-7 items-center rounded-sm px-2 text-sm text-text no-underline hover:bg-accent-soft"
                  >
                    Open the source
                  </a>
                )}
                {canWrite && (
                  <>
                    <button
                      type="button"
                      role="menuitem"
                      data-testid={refsTestIds.panelMenuMerge}
                      onClick={() => {
                        setMenuOpen(false);
                        onMerge();
                      }}
                      className="flex min-h-7 cursor-pointer items-center rounded-sm border-0 bg-transparent px-2 text-left text-sm text-text hover:bg-accent-soft"
                    >
                      Merge into…
                    </button>
                    <button
                      type="button"
                      role="menuitem"
                      data-testid={refsTestIds.panelMenuRemove}
                      onClick={() => {
                        setMenuOpen(false);
                        onRemove();
                      }}
                      className="flex min-h-7 cursor-pointer items-center rounded-sm border-0 bg-transparent px-2 text-left text-sm text-danger hover:bg-accent-soft"
                    >
                      Remove source
                    </button>
                  </>
                )}
              </div>
            )}
          </div>
        )}
        <button
          type="button"
          aria-label="Close source"
          title="Close"
          data-testid={refsTestIds.panelClose}
          onClick={onClose}
          className={iconButton}
        >
          <RefIcon name="close" />
        </button>
      </div>
      <h2
        data-testid={refsTestIds.panelTitle}
        className="mt-1 mb-0.5 text-md leading-[1.3] font-semibold text-text"
      >
        {source.title}
      </h2>
      <div className="text-xs text-muted">{byline}</div>
    </header>
  );
}
