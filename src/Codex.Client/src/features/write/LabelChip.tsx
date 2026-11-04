import type { LabelRef } from '@codex/core/api/structure';

const dots: Record<string, string> = {
  ok: 'bg-ok',
  info: 'bg-info',
  warn: 'bg-warn',
  danger: 'bg-danger',
};

interface LabelChipProps {
  label: LabelRef;
  testId: string;
}

// A label is a dot in a semantic colour plus a name. A colour the design has no tone for gets a hollow dot,
// so it is visibly not one of the four.
export function LabelChip({ label, testId }: LabelChipProps) {
  const tone = label.color !== null && label.color in dots ? label.color : 'unknown';
  return (
    <span
      data-testid={testId}
      data-tone={tone}
      className="inline-flex h-[17px] items-center gap-1 rounded-full bg-surface-2 px-1.5 text-2xs font-medium whitespace-nowrap"
    >
      <i
        aria-hidden="true"
        className={`size-1.5 rounded-full ${tone === 'unknown' ? 'border border-muted' : dots[tone]}`}
      />
      {label.name}
    </span>
  );
}
