import type { LabelRef } from '@codex/core/api/structure';
import { toneOf } from '../../editing/labelTone';
import { planTestIds } from './testIds';

const dot = { ok: 'bg-ok', info: 'bg-info', warn: 'bg-warn', danger: 'bg-danger' } as const;

// A label is a dot in a semantic colour plus a name. A colour outside ok/info/warn/danger is drawn as a hollow dot
// with the colour in its tooltip, so it is visibly "unknown" rather than quietly shown as some other colour.
export function LabelPill({ sceneId, label }: { sceneId: string; label: LabelRef }) {
  const tone = toneOf(label.color);
  return (
    <span
      data-testid={planTestIds.label(sceneId, label.id)}
      data-tone={tone ?? 'unknown'}
      title={tone === null ? `Unrecognised label colour: ${label.color ?? 'none'}` : undefined}
      className="inline-flex h-[17px] items-center gap-1 rounded-full bg-surface-2 px-1.5 text-2xs font-medium whitespace-nowrap text-text"
    >
      <span
        aria-hidden="true"
        className={`size-1.5 flex-none rounded-full ${tone === null ? 'border border-muted' : dot[tone]}`}
      />
      {label.name}
    </span>
  );
}
