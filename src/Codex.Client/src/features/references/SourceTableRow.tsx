import { useEffect, useRef, type CSSProperties } from 'react';
import type { SourceRow } from '@codex/core/api/referenceSources';
import { columnClass } from './columns';
import { kindIcons, RefIcon } from './RefIcon';
import { lookOf, reasonLabel } from './sourceStatus';
import { refsTestIds } from './testIds';

const cellClass =
  'h-[calc(var(--row-height)+10px)] border-b border-border px-2 py-1 align-middle group-hover:bg-[color-mix(in_srgb,var(--color-accent-soft)_55%,transparent)] group-aria-selected:bg-accent-soft';

// One source: marker number, title with author · year · publisher, the optional columns, status and reason.
export function SourceTableRow({
  source,
  selected,
  onSelect,
}: {
  source: SourceRow;
  selected: boolean;
  onSelect: () => void;
}) {
  const row = useRef<HTMLTableRowElement>(null);
  useEffect(() => {
    if (selected) row.current?.scrollIntoView({ block: 'nearest' });
  }, [selected]);

  const look = lookOf(source.status);
  const tone = { '--tone': look?.tone ?? 'var(--color-muted)' } as CSSProperties;
  const byline = [
    [source.authors, source.year].filter((part) => part !== '' && part !== null).join(' · '),
    source.publisher,
  ]
    .filter((part) => part !== '' && part !== null)
    .join(' · ');
  const quotedShare =
    source.limit === 0 ? 0 : Math.min(100, (source.quotedWords / source.limit) * 100);
  const kindIcon = kindIcons[source.kind];
  const reason = reasonLabel(source);

  return (
    <tr
      ref={row}
      tabIndex={0}
      aria-selected={selected}
      data-testid={refsTestIds.row(source.id)}
      data-status={source.status}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.key === 'Enter') onSelect();
      }}
      className="group cursor-pointer outline-offset-[-2px] focus-visible:outline-2 focus-visible:outline-accent"
    >
      <td
        className={`${cellClass} w-10 pl-3 group-aria-selected:shadow-[inset_2px_0_0_var(--color-accent)]`}
      >
        <span
          data-testid={refsTestIds.rowNumber}
          style={tone}
          className="inline-block min-w-[18px] rounded-[3px] bg-[color-mix(in_srgb,var(--tone)_16%,transparent)] px-[3px] text-center text-2xs leading-[1.5] font-semibold text-[var(--tone)] tabular-nums"
        >
          {source.number ?? '–'}
        </span>
      </td>
      <td className={`${cellClass} max-w-0`}>
        <b className="block truncate font-medium text-text">{source.title}</b>
        <span className="block truncate text-xs text-muted">{byline}</span>
      </td>
      <td className={`${cellClass} ${columnClass.kind}`}>
        <span className="inline-flex items-center gap-[5px] text-xs whitespace-nowrap text-muted">
          {kindIcon && <RefIcon name={kindIcon} />}
          {source.kindLabel}
        </span>
      </td>
      <td className={`${cellClass} ${columnClass.usedIn} text-xs whitespace-nowrap tabular-nums`}>
        {source.uses === 0 ? (
          <span className="text-muted">None</span>
        ) : (
          <>
            {source.uses} {source.uses === 1 ? 'use' : 'uses'}
            <span className="text-muted"> · {source.usedIn}</span>
          </>
        )}
      </td>
      <td className={`${cellClass} ${columnClass.quoted} text-right`}>
        <div className="grid justify-items-end gap-[3px] text-xs whitespace-nowrap tabular-nums">
          {source.quotedWords === 0 ? (
            <span className="text-muted">–</span>
          ) : (
            <>
              {source.quotedWords} words
              <span className="h-[3px] w-14 overflow-hidden rounded-[2px] bg-surface-2">
                <i
                  className={`block h-full ${source.quotedWords > source.limit ? 'bg-danger' : 'bg-accent'}`}
                  style={{ width: `${quotedShare}%` }}
                />
              </span>
            </>
          )}
        </div>
      </td>
      <td className={`${cellClass} ${columnClass.licence}`}>
        <span className="block max-w-[140px] truncate text-xs">{source.licenceLabel}</span>
      </td>
      <td className={`${cellClass} w-[210px]`}>
        <span
          data-testid={refsTestIds.rowStatus}
          className="inline-flex items-center gap-[5px] text-xs font-medium whitespace-nowrap text-text"
        >
          <i className={`size-1.5 shrink-0 rounded-full ${look?.dot ?? ''}`} aria-hidden />
          {look?.label ?? source.status}
        </span>
        <span
          data-testid={refsTestIds.rowReason}
          title={reason}
          className="block max-w-[200px] truncate pl-[11px] text-2xs text-muted"
        >
          {reason}
        </span>
        {source.reminderLabel !== null && (
          <span
            data-testid={refsTestIds.rowReminder}
            className={`block max-w-[200px] truncate pl-[11px] text-2xs ${source.reminderDue ? 'font-medium text-warn' : 'text-muted'}`}
          >
            {source.reminderLabel}
          </span>
        )}
      </td>
    </tr>
  );
}
