import { useState, type FormEvent } from 'react';
import { editingTestIds } from './testIds';

export type LabelTone = 'ok' | 'info' | 'warn' | 'danger';

export interface PickerLabel {
  id: string;
  name: string;
  /** Null for a stored colour that is not one of the four tones: drawn as a hollow dot, still pickable. */
  tone: LabelTone | null;
}

interface LabelPickerProps {
  labels: PickerLabel[];
  // Ids of the selected labels.
  selected: string[];
  onChange: (selected: string[]) => void;
  // When given, the picker offers a "new label" field; the lane creates it and adds it to `labels`.
  onCreate?: (name: string) => void;
}

const dotTones: Record<LabelTone, string> = {
  ok: 'bg-ok',
  info: 'bg-info',
  warn: 'bg-warn',
  danger: 'bg-danger',
};

// A label is a dot in a semantic colour (hollow when the colour is unknown) plus a name. Toggling one emits the new
// selection.
export function LabelPicker({ labels, selected, onChange, onCreate }: LabelPickerProps) {
  const [draft, setDraft] = useState('');

  const toggle = (id: string) =>
    onChange(selected.includes(id) ? selected.filter((s) => s !== id) : [...selected, id]);

  const create = (event: FormEvent) => {
    event.preventDefault();
    const name = draft.trim();
    if (name === '' || !onCreate) return;
    onCreate(name);
    setDraft('');
  };

  return (
    <div
      data-testid={editingTestIds.labelPicker}
      role="group"
      aria-label="Labels"
      className="flex flex-col gap-1"
    >
      {labels.map((label) => {
        const isOn = selected.includes(label.id);
        return (
          <button
            key={label.id}
            type="button"
            role="checkbox"
            aria-checked={isOn}
            data-testid={editingTestIds.labelOption(label.id)}
            data-tone={label.tone ?? 'unknown'}
            className="flex items-center gap-2 rounded-sm px-2 py-1 text-left text-sm text-text hover:bg-surface-2"
            onClick={() => toggle(label.id)}
          >
            <span
              className={`size-2 rounded-full ${label.tone === null ? 'border border-muted' : dotTones[label.tone]}`}
              aria-hidden="true"
            />
            <span className="flex-1">{label.name}</span>
            {isOn ? <span aria-hidden="true">✓</span> : null}
          </button>
        );
      })}
      {onCreate ? (
        <form onSubmit={create}>
          <input
            data-testid={editingTestIds.labelCreate}
            aria-label="New label"
            placeholder="New label…"
            value={draft}
            className="h-[var(--control-height)] w-full rounded-sm border border-border bg-surface px-2 text-sm text-text placeholder:text-muted"
            onChange={(event) => setDraft(event.target.value)}
          />
        </form>
      ) : null}
    </div>
  );
}
