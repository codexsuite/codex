import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { invalidateOutline } from '@codex/core/api/outline';
import { structureApi, type Label } from '@codex/core/api/structure';
import { planTestIds } from './testIds';

const rowInput =
  'h-7 min-w-0 flex-1 rounded-sm border border-border bg-bg px-2 text-sm text-text outline-none focus:border-accent';
const smallButton =
  'h-7 shrink-0 rounded-sm border border-border bg-transparent px-2 text-xs text-text hover:bg-accent-soft';

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

const plural = (n: number) => `${n} ${n === 1 ? 'scene' : 'scenes'}`;

// The panel behind `Manage labels` in the Plan label filter: rename a label (Enter or leaving the field), delete one
// after the dialog says how many scenes lose it, and add a new one. A name the book already has is refused by the
// server and the message shows under the list.
export function LabelManager({
  onDone,
  onDeleted,
}: {
  onDone: () => void;
  /** A label was deleted: the filter drops it if it was the picked one. */
  onDeleted: (labelId: string) => void;
}) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const labels = useQuery({
    queryKey: ['labels', bookId],
    queryFn: ({ signal }) => structureApi.labels(bookId, { signal }),
  });
  const [error, setError] = useState<string | null>(null);
  const [asking, setAsking] = useState<Label | null>(null);
  const [draft, setDraft] = useState('');

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['labels', bookId] });
    await invalidateOutline(queryClient, bookId);
  };
  const guard = async (action: () => Promise<unknown>): Promise<boolean> => {
    try {
      await action();
      setError(null);
      return true;
    } catch (e) {
      setError(reasonOf(e));
      return false;
    }
  };
  const rename = async (label: Label, name: string, field: HTMLInputElement) => {
    const text = name.trim();
    if (text === label.name) return;
    if (text === '') {
      field.value = label.name;
      setError('A label needs a name.');
      return;
    }
    if (await guard(async () => structureApi.patchLabel(label.id, { name: text, color: null })))
      await refresh();
    else field.value = label.name;
  };
  const create = async () => {
    const name = draft.trim();
    if (name === '') return;
    if (await guard(() => structureApi.createLabel(bookId, { name, color: 'info' }))) {
      setDraft('');
      await refresh();
    }
  };
  const remove = async (label: Label) => {
    if (await guard(() => structureApi.deleteLabel(label.id))) {
      setAsking(null);
      onDeleted(label.id);
      await refresh();
    }
  };

  return (
    <div
      data-testid={planTestIds.labelManager}
      className="overflow-auto p-2"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && asking) {
          e.stopPropagation();
          setAsking(null);
        }
      }}
    >
      {labels.data?.map((label) => (
        <div key={label.id} className="mb-1 flex items-center gap-1">
          <input
            type="text"
            data-testid={planTestIds.labelRename(label.id)}
            aria-label={`Rename ${label.name}`}
            defaultValue={label.name}
            key={`${label.id}:${label.name}`}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.currentTarget.blur();
            }}
            onBlur={(e) => void rename(label, e.target.value, e.target)}
            className={rowInput}
          />
          <button
            type="button"
            data-testid={planTestIds.labelDelete(label.id)}
            aria-label={`Delete ${label.name}`}
            onClick={() => setAsking(label)}
            className={`${smallButton} text-danger`}
          >
            Delete
          </button>
        </div>
      ))}
      {asking && (
        <div
          role="alertdialog"
          aria-label={`Delete ${asking.name}`}
          className="my-1 rounded-sm border border-border bg-surface-2 p-2"
        >
          <p data-testid={planTestIds.labelDeleteMessage} className="m-0 text-sm">
            Used on {plural(asking.sceneCount)}. Deleting removes it from{' '}
            {asking.sceneCount === 1 ? 'it' : 'them'}.
          </p>
          <div className="mt-2 flex justify-end gap-2">
            <button
              type="button"
              autoFocus
              data-testid={planTestIds.labelDeleteCancel}
              onClick={() => setAsking(null)}
              className={smallButton}
            >
              Cancel
            </button>
            <button
              type="button"
              data-testid={planTestIds.labelDeleteConfirm}
              onClick={() => void remove(asking)}
              className="h-7 rounded-sm border-0 bg-danger px-3 text-xs text-white"
            >
              Delete
            </button>
          </div>
        </div>
      )}
      <div className="mt-2 flex items-center gap-1">
        <input
          type="text"
          data-testid={planTestIds.labelNewName}
          aria-label="New label name"
          placeholder="New label"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void create();
          }}
          className={rowInput}
        />
        <button
          type="button"
          data-testid={planTestIds.labelNewCreate}
          onClick={() => void create()}
          className={smallButton}
        >
          Add
        </button>
      </div>
      {error && (
        <p
          data-testid={planTestIds.labelError}
          role="alert"
          className="m-0 mt-1 text-xs text-danger"
        >
          {error}
        </p>
      )}
      <button
        type="button"
        data-testid={planTestIds.labelManagerDone}
        onClick={onDone}
        className={`${smallButton} mt-2`}
      >
        Done
      </button>
    </div>
  );
}
