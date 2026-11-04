import type { SeriesOrderEntry } from '@codex/core/api/settings';
import { padded } from '@codex/core/features/settings/seriesNumbers';
import { settingsTestIds } from './testIds';

interface SeriesOrderListProps {
  order: SeriesOrderEntry[];
  currentId: string;
  /** book id -> the number it had before the last change */
  was: Map<string, number>;
}

// The series in order with this book highlighted, `was 003` beside renumbered books; a published book that
// moved is marked in warn.
export function SeriesOrderList({ order, currentId, was }: SeriesOrderListProps) {
  const movedPublished = order.some((e) => e.status === 'published' && was.has(e.id));
  return (
    <div className="w-full">
      <ol data-testid={settingsTestIds.order} className="m-0 flex list-none flex-col gap-1 p-0">
        {order.map((entry) => {
          const before = was.get(entry.id);
          return (
            <li
              key={entry.id}
              data-testid={settingsTestIds.orderRow(entry.id)}
              aria-current={entry.id === currentId ? 'true' : undefined}
              className="flex items-center gap-2 rounded border border-border px-2 py-1 text-sm aria-[current=true]:border-accent aria-[current=true]:bg-accent-soft"
            >
              <span className="w-10 text-muted">{padded(entry.number)}</span>
              <span className="flex-1 truncate">{entry.title}</span>
              {before !== undefined && (
                <span
                  data-testid={settingsTestIds.orderWas(entry.id)}
                  data-tone={entry.status === 'published' ? 'warn' : 'muted'}
                  className="text-xs text-muted data-[tone=warn]:text-warn"
                >
                  was {padded(before)}
                </span>
              )}
              <span className="text-xs capitalize text-muted">{entry.status}</span>
            </li>
          );
        })}
      </ol>
      {movedPublished && (
        <p
          data-testid={settingsTestIds.orderPublishedWarning}
          className="m-0 mt-1 text-xs text-warn"
        >
          A published book was renumbered. Its printed number no longer matches.
        </p>
      )}
    </div>
  );
}
