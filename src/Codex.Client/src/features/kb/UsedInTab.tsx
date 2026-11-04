import { Link } from 'react-router-dom';
import type { KbUse } from '@codex/core/api/kbEntries';
import { writeLink } from '@codex/core/shell/links';
import { kindLabel } from '@codex/core/features/kb/kindLabels';
import { kbTestIds } from './testIds';
import { UsedInRow } from './UsedInRow';
import { useUseActions } from './useUseActions';

interface UsedInTabProps {
  bookId: string;
  entryId: string;
  uses: KbUse[];
}

const chapterKey = (use: KbUse) => `${use.bookId}|${use.chapterNumber ?? 'x'}|${use.chapterTitle}`;

// Every scene that touches the entry in book order, grouped by chapter, with a tally by kind and the link that
// highlights the entry in Write.
export function UsedInTab({ bookId, entryId, uses }: UsedInTabProps) {
  const actions = useUseActions(entryId);
  const tally = new Map<string, number>();
  for (const use of uses)
    for (const k of use.kinds) tally.set(k.kind, (tally.get(k.kind) ?? 0) + 1);
  const groups: { key: string; title: string; uses: KbUse[] }[] = [];
  for (const use of uses) {
    const key = chapterKey(use);
    const last = groups[groups.length - 1];
    if (last?.key === key) last.uses.push(use);
    else
      groups.push({
        key,
        title:
          use.chapterNumber === null
            ? use.chapterTitle
            : `Chapter ${use.chapterNumber}${use.chapterTitle ? ` · ${use.chapterTitle}` : ''}`,
        uses: [use],
      });
  }

  return (
    <div>
      <div className="flex items-center gap-3 border-b border-border px-4 py-2">
        <span data-testid={kbTestIds.usedInTally} className="flex-1 text-xs text-muted">
          {uses.length} {uses.length === 1 ? 'scene' : 'scenes'}
          {[...tally].map(([kind, count]) => ` · ${count} ${kindLabel(kind)}`).join('')}
        </span>
        <Link
          to={writeLink(bookId, { kb: entryId })}
          data-testid={kbTestIds.highlightInWrite}
          className="text-xs font-medium text-accent no-underline hover:underline"
        >
          Highlight in Write
        </Link>
      </div>
      {actions.error && (
        <p
          data-testid={kbTestIds.useError}
          role="alert"
          className="m-0 px-4 py-2 text-xs text-danger"
        >
          {actions.error}
        </p>
      )}
      {uses.length === 0 && (
        <p data-testid={kbTestIds.usedInEmpty} className="m-0 px-4 py-3 text-sm text-muted">
          No scene uses this entry yet.
        </p>
      )}
      {groups.map((group) => (
        <section key={group.key}>
          <h4 className="m-0 px-4 pt-3 pb-1 text-2xs font-medium text-muted uppercase">
            {group.title}
          </h4>
          <ul className="m-0 list-none p-0">
            {group.uses.map((use) => (
              <UsedInRow
                key={use.sceneId}
                use={use}
                onMakeMention={(u) => void actions.makeMention(u)}
                onUnlink={(u) => void actions.unlink(u)}
              />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}
