import type { CSSProperties } from 'react';
import type { BridgeChip } from '@codex/core/editing/editorBridge';
import { mobileEditorTestIds } from '../testIds';

// A Knowledge base entry on the card: a square in its category colour, then its name.
export function KbChip({ sceneId, chip }: { sceneId: string; chip: BridgeChip }) {
  const style = {
    '--cat': `var(--category-${chip.category}, var(--color-muted))`,
  } as CSSProperties;
  return (
    <span
      className="me-kchip"
      style={style}
      data-testid={mobileEditorTestIds.sceneChip(sceneId, chip.id)}
    >
      <i aria-hidden="true" />
      {chip.name}
    </span>
  );
}
