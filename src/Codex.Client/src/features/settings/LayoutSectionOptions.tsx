import type { LayoutOptionValue, LayoutTemplate } from '@codex/core/api/layout';
import { FieldRow } from './FieldRow';
import { settingsTestIds } from './testIds';

// The chosen template's options, drawn from its manifest's options schema: a boolean is Off / On, a choice one button
// per value. A press stores the value for this book (`PUT …/layout`); the Layout view shows the same options.
export function LayoutSectionOptions({
  template,
  onOption,
}: {
  template: LayoutTemplate;
  onOption: (key: string, value: LayoutOptionValue) => void;
}) {
  if (template.options.length === 0) return null;
  return (
    <FieldRow
      label="Options"
      help={`What ${template.name} lets each book change. Your text stays as it is.`}
    >
      <div className="flex basis-full flex-col gap-2">
        {template.options.map((option) => {
          const choices: LayoutOptionValue[] =
            option.type === 'boolean' ? [false, true] : option.choices;
          return (
            <div
              key={option.key}
              data-testid={settingsTestIds.layoutOption(option.key)}
              className="flex flex-wrap items-center gap-x-3 gap-y-1"
            >
              <span className="min-w-0 flex-1 text-sm text-text">{option.label}</span>
              <div
                role="group"
                aria-label={option.label}
                className="inline-flex overflow-hidden rounded border border-border"
              >
                {choices.map((choice) => (
                  <button
                    key={String(choice)}
                    type="button"
                    data-testid={settingsTestIds.layoutOptionChoice(option.key, String(choice))}
                    aria-pressed={option.value === choice}
                    onClick={() => onOption(option.key, choice)}
                    className="h-7 cursor-pointer border-0 border-r border-border bg-surface px-2.5 text-sm text-muted last:border-r-0 aria-[pressed=true]:bg-accent-soft aria-[pressed=true]:text-text"
                  >
                    {typeof choice === 'boolean' ? (choice ? 'On' : 'Off') : choice}
                  </button>
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </FieldRow>
  );
}
