import type { CSSProperties } from 'react';
import type { EntryRef } from '@codex/core/api/structure';
import { planTestIds } from './testIds';
import { usePlanEdit } from './usePlanEdit';

// A Knowledge base entry linked to a scene: tinted with its category colour, with the category's square before the name.
export function EntryChip({ sceneId, entry }: { sceneId: string; entry: EntryRef }) {
  const { colours } = usePlanEdit();
  return (
    <span
      data-testid={planTestIds.entry(sceneId, entry.id)}
      className="plan-cx"
      style={{ '--cat': colours.get(entry.id) ?? 'var(--color-accent)' } as CSSProperties}
    >
      <i aria-hidden="true" />
      {entry.name}
    </span>
  );
}
