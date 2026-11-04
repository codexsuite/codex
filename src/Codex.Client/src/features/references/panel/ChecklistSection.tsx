import type { CSSProperties } from 'react';
import type { ClearanceCheck } from '@codex/core/api/referenceSources';
import { RefIcon, type RefIconName } from '../RefIcon';
import { refsTestIds } from '../testIds';

const tones = {
  ok: 'var(--color-ok)',
  warn: 'var(--color-warn)',
  danger: 'var(--color-danger)',
} as const;
const marks: Record<ClearanceCheck['status'], RefIconName> = {
  ok: 'check',
  warn: 'bang',
  danger: 'cross',
};

// The checks behind the verdict: prose quoted against the limit, lyrics, figures, data, licence, citation, identifiers.
export function ChecklistSection({ checks }: { checks: ClearanceCheck[] }) {
  return (
    <ul data-testid={refsTestIds.checks} className="m-0 mt-2 list-none p-0">
      {checks.map((check) => (
        <li
          key={check.key}
          data-testid={refsTestIds.check(check.key)}
          data-status={check.status}
          style={{ '--tone': tones[check.status] } as CSSProperties}
          className="grid grid-cols-[18px_minmax(0,1fr)] items-start gap-x-2 border-b border-dashed border-border py-1.5 text-sm last:border-b-0"
        >
          <span
            aria-hidden
            className="mt-px grid size-4 place-items-center rounded-full bg-[color-mix(in_srgb,var(--tone)_16%,transparent)] text-[var(--tone)]"
          >
            <RefIcon name={marks[check.status]} className="size-2.5" />
          </span>
          <span>
            <b className="font-medium text-text">{check.title}</b>
            <span className="block text-xs text-muted">{check.detail}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}
