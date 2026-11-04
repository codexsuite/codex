import type { LayoutTemplate } from '@codex/core/api/settings';
import { FieldRow } from './FieldRow';
import { settingsTestIds } from './testIds';

interface LayoutSectionTrimsProps {
  template: LayoutTemplate;
  /** Picks a trim size; without it the sizes are shown but cannot be changed. */
  onTrim?: (trim: string) => void;
}

// The trim sizes the template supports with the book's current one marked, and a press changes it once Layout is loaded.
export function LayoutSectionTrims({ template, onTrim }: LayoutSectionTrimsProps) {
  return (
    <FieldRow
      label="Trim size"
      help={`Sizes ${template.name} supports. Bleed, crop marks and image quality are chosen for each export.`}
    >
      <div
        role="group"
        aria-label="Trim size"
        className="inline-flex overflow-hidden rounded border border-border"
      >
        {template.trims.map((trim) =>
          onTrim ? (
            <button
              key={trim}
              type="button"
              data-testid={settingsTestIds.layoutTrim(trim)}
              aria-current={template.trimSize === trim}
              onClick={() => onTrim(trim)}
              className="h-8 cursor-pointer border-0 border-r border-border bg-surface px-3 text-base text-muted last:border-r-0 aria-[current=true]:bg-accent-soft aria-[current=true]:text-text"
            >
              {trim}
            </button>
          ) : (
            <span
              key={trim}
              data-testid={settingsTestIds.layoutTrim(trim)}
              aria-current={template.trimSize === trim}
              className="h-8 border-r border-border bg-surface px-3 text-base leading-8 text-muted last:border-r-0 aria-[current=true]:bg-accent-soft aria-[current=true]:text-text"
            >
              {trim}
            </span>
          ),
        )}
      </div>
    </FieldRow>
  );
}
