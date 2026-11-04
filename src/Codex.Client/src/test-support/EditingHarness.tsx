import { editingHarnessIds } from './editingHarnessIds';
import { useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { LabelPicker, type PickerLabel } from '../editing/LabelPicker';
import { SaveStatus } from '../editing/SaveStatus';
import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { showToast } from '@codex/core/editing/toastStore';
import { ToastHost } from '../editing/Toast';
import { useAutosave } from '../editing/useAutosave';

// Mounted only by the component specs in this folder: a textarea wired to useAutosave with a scripted save.
export type SaveBehavior = 'ok' | 'conflict' | 'offline';

export function AutosaveHarness({ behavior }: { behavior: SaveBehavior }) {
  const [value, setValue] = useState('');
  const [version, setVersion] = useState(1);
  const [saves, setSaves] = useState(0);
  const { flush } = useAutosave({
    value,
    version,
    save: (_value, current) => {
      setSaves((n) => n + 1);
      if (behavior === 'conflict') {
        return Promise.reject(new ApiRequestError('Stale', 409, 'conflict'));
      }
      if (behavior === 'offline') return Promise.reject(new TypeError('Failed to fetch'));
      return Promise.resolve({ version: current + 1 });
    },
  });
  return (
    <div>
      <SaveStatus onReload={() => setVersion((v) => v + 1)} />
      <textarea
        data-testid={editingHarnessIds.input}
        value={value}
        onChange={(event) => setValue(event.target.value)}
        onBlur={flush}
      />
      <span data-testid={editingHarnessIds.saves}>{saves}</span>
      <button
        type="button"
        data-testid={editingHarnessIds.reload}
        onClick={() => saveStatusStore.setRemoteChange()}
      >
        remote
      </button>
    </div>
  );
}

export function LabelPickerHarness({ labels }: { labels: PickerLabel[] }) {
  const [selected, setSelected] = useState<string[]>([]);
  return (
    <div>
      <LabelPicker labels={labels} selected={selected} onChange={setSelected} />
      <output data-testid={editingHarnessIds.selected}>{selected.join(',')}</output>
    </div>
  );
}

export function ToastHarness({ withUndo }: { withUndo: boolean }) {
  const [undone, setUndone] = useState(0);
  return (
    <div>
      <ToastHost />
      <button
        type="button"
        data-testid={editingHarnessIds.show}
        onClick={() =>
          showToast({
            message: 'Archived scene',
            undo: withUndo ? () => Promise.resolve(setUndone((n) => n + 1)) : undefined,
          })
        }
      >
        show
      </button>
      <output data-testid={editingHarnessIds.undone}>{undone}</output>
    </div>
  );
}
