import { ChevronDownIcon } from './icons';

export interface ToolbarSelectOption {
  value: string;
  label: string;
}

interface ToolbarSelectProps {
  /** The muted word inside the control ("Sort"); it is also the select's accessible name. */
  label: string;
  testId: string;
  value: string;
  options: readonly ToolbarSelectOption[];
  onChange: (value: string) => void;
}

// design/screens/library.html `.sel`: one bordered control holding the muted label, the value and a chevron. The
// value is a native <select> drawn without its own chrome, so keyboard, screen readers and tests get a real select.
export function ToolbarSelect({ label, testId, value, options, onChange }: ToolbarSelectProps) {
  return (
    <span className="relative inline-flex h-[var(--control-height)] items-center gap-1 rounded-sm border border-border bg-surface px-2 text-sm whitespace-nowrap focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent">
      <em className="text-muted not-italic">{label}</em>
      <select
        data-testid={testId}
        aria-label={label}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field-sizing-content cursor-pointer appearance-none border-0 bg-transparent py-0 pr-[15px] pl-0 text-text outline-none"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <span className="pointer-events-none absolute right-2 flex size-[11px] text-muted [&>svg]:size-full">
        <ChevronDownIcon />
      </span>
    </span>
  );
}
