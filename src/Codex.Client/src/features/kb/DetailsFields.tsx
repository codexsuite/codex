import type { KeyboardEvent } from 'react';
import type { KbFieldDef } from '@codex/core/api/kb';
import type { KbCustomField } from '@codex/core/api/kbEntries';
import { PlusIcon } from './icons';
import { inlineField, miniButton, railHeading } from './kbStyles';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface DetailsFieldsProps {
  defs: KbFieldDef[];
  values: Record<string, string>;
  custom: KbCustomField[];
  onValue: (key: string, value: string) => void;
  onCustom: (fields: KbCustomField[]) => void;
}

// `.fv`: a value that reads as text and edits in place. `.fk`: its label.
// A value wraps to as many lines as it needs but stays one line of text: Enter does not add a break.
const noNewline = (event: KeyboardEvent<HTMLTextAreaElement>) => {
  if (event.key === 'Enter') event.preventDefault();
};
const valueInput = `resize-none [field-sizing:content] -mx-1 w-[calc(100%+8px)] min-w-0 rounded-xs px-1 py-[3px] text-sm text-text placeholder:text-muted ${inlineField}`;
const labelInput = `-ml-1 w-20 min-w-0 rounded-xs px-1 py-[3px] text-xs text-muted placeholder:text-muted ${inlineField}`;

// The rail's Details: a label / value grid of the category's fields, then the author's own `+ Field` rows.
export function DetailsFields({ defs, values, custom, onValue, onCustom }: DetailsFieldsProps) {
  // Below write (#80) the fields read only.
  const canWrite = useCanWrite();
  const setCustom = (index: number, change: Partial<KbCustomField>) =>
    onCustom(custom.map((f, i) => (i === index ? { ...f, ...change } : f)));
  return (
    <section aria-label="Details">
      <h4 className={`${railHeading} mb-1.5`}>
        Details
        {canWrite && (
          <button
            type="button"
            data-testid={kbTestIds.addCustomField}
            onClick={() => onCustom([...custom, { label: '', value: '' }])}
            className={`${miniButton} ml-auto tracking-normal normal-case`}
          >
            <PlusIcon size={11} />
            Field
          </button>
        )}
      </h4>
      <div className="grid grid-cols-[minmax(64px,auto)_minmax(0,1fr)] items-baseline gap-x-2 gap-y-0.5 text-sm">
        {defs.map((def) => (
          <label key={def.key} className="contents">
            <span className="px-0 py-[3px] text-xs text-muted">{def.label}</span>
            <textarea
              rows={1}
              data-testid={kbTestIds.fieldInput(def.key)}
              placeholder={canWrite ? 'Add…' : undefined}
              readOnly={!canWrite}
              value={values[def.key] ?? ''}
              onChange={(event) => onValue(def.key, event.target.value)}
              onKeyDown={noNewline}
              className={valueInput}
            />
          </label>
        ))}
        {custom.map((field, index) => (
          <div key={index} className="contents">
            <input
              type="text"
              data-testid={kbTestIds.customFieldLabel(index)}
              aria-label="Field name"
              placeholder="Field"
              readOnly={!canWrite}
              value={field.label}
              onChange={(event) => setCustom(index, { label: event.target.value })}
              className={labelInput}
            />
            <textarea
              rows={1}
              data-testid={kbTestIds.customFieldValue(index)}
              aria-label="Field value"
              placeholder={canWrite ? 'Add…' : undefined}
              readOnly={!canWrite}
              value={field.value}
              onChange={(event) => setCustom(index, { value: event.target.value })}
              onKeyDown={noNewline}
              className={valueInput}
            />
          </div>
        ))}
      </div>
      {defs.length === 0 && custom.length === 0 && (
        <p className="m-0 text-xs text-muted italic">No fields for this category yet.</p>
      )}
    </section>
  );
}
