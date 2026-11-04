import type { ReactNode } from 'react';
import { refsTestIds } from '../testIds';

// One labelled row of the details form. A required field that is empty shows warn.
export function DetailField({
  name,
  label,
  value,
  missing,
  mono,
  disabled,
  trailing,
  onChange,
  onCommit,
}: {
  name: string;
  label: string;
  value: string;
  missing: boolean;
  mono?: boolean;
  disabled: boolean;
  trailing?: ReactNode;
  onChange: (value: string) => void;
  onCommit: () => void;
}) {
  const id = `refs-field-input-${name}`;
  return (
    <>
      <label htmlFor={id} className="pt-1 text-xs text-muted">
        {label}
      </label>
      <div className="flex items-center gap-2">
        <input
          id={id}
          data-testid={refsTestIds.field(name)}
          data-missing={missing}
          value={value}
          disabled={disabled}
          placeholder={missing ? 'Required for the citation' : ''}
          spellCheck={false}
          onChange={(event) => onChange(event.target.value)}
          onBlur={onCommit}
          onKeyDown={(event) => {
            if (event.key === 'Enter') event.currentTarget.blur();
          }}
          className={`min-w-0 flex-1 rounded-sm border bg-surface px-2 py-1 text-sm text-text ${
            missing ? 'border-warn' : 'border-border'
          } ${mono ? 'font-mono' : ''}`}
        />
        {trailing}
      </div>
    </>
  );
}
