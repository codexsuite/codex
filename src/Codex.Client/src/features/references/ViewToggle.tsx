import { refsTestIds } from './testIds';

export type ReferencesView = 'sources' | 'bibliography';

const views: { value: ReferencesView; label: string; testId: string }[] = [
  { value: 'sources', label: 'Sources', testId: refsTestIds.viewSources },
  { value: 'bibliography', label: 'Bibliography', testId: refsTestIds.viewBibliography },
];

// The Sources / Bibliography segmented control.
export function ViewToggle({
  value,
  onChange,
}: {
  value: ReferencesView;
  onChange: (view: ReferencesView) => void;
}) {
  return (
    <div
      role="group"
      aria-label="View"
      className="inline-flex h-[var(--control-height)] shrink-0 overflow-hidden rounded-sm border border-border bg-surface"
    >
      {views.map((view) => (
        <button
          key={view.value}
          type="button"
          aria-pressed={value === view.value}
          data-testid={view.testId}
          onClick={() => onChange(view.value)}
          className="inline-flex cursor-pointer items-center border-0 border-r border-border bg-transparent px-[9px] text-sm whitespace-nowrap text-muted last:border-r-0 aria-pressed:bg-accent-soft aria-pressed:font-semibold aria-pressed:text-text"
        >
          {view.label}
        </button>
      ))}
    </div>
  );
}
