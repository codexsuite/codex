import { useState } from 'react';
import { useOutline } from '../../api/outlineQueries';
import { kbTestIds } from './testIds';

interface TimelineAddFormProps {
  bookId: string;
  onAdd: (sceneId: string, sceneNumber: string, text: string) => Promise<void>;
  onClose: () => void;
}

// The form: a scene to put the note at (it becomes a Codex addition at the start of that scene) and its text.
export function TimelineAddForm({ bookId, onAdd, onClose }: TimelineAddFormProps) {
  const outline = useOutline(bookId);
  const [sceneId, setSceneId] = useState('');
  const [text, setText] = useState('');
  const [error, setError] = useState<string | null>(null);

  const scenes = (outline.data?.acts ?? [])
    .flatMap((act) => act.chapters)
    .filter((chapter) => !chapter.archived)
    .flatMap((chapter) => chapter.scenes);

  const chosen = scenes.find((s) => s.id === sceneId) ?? scenes[0];
  const submit = async () => {
    if (!chosen) return;
    try {
      await onAdd(chosen.id, chosen.number, text);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That note could not be added.');
    }
  };

  return (
    <div data-testid={kbTestIds.timelineAddForm} className="flex flex-col gap-1 px-4 py-2">
      <label className="flex flex-col gap-1 text-2xs font-medium text-muted uppercase">
        Scene
        <select
          data-testid={kbTestIds.timelineAddScene}
          value={chosen?.id ?? ''}
          onChange={(event) => setSceneId(event.target.value)}
          className="rounded border border-border bg-bg p-1 text-sm font-normal text-text normal-case"
        >
          {scenes.map((s) => (
            <option key={s.id} value={s.id}>
              {s.title ? `Scene ${s.number} · ${s.title}` : `Scene ${s.number}`}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-2xs font-medium text-muted uppercase">
        Note
        <textarea
          data-testid={kbTestIds.timelineAddText}
          rows={2}
          value={text}
          onChange={(event) => setText(event.target.value)}
          className="rounded border border-border bg-bg p-2 text-sm font-normal text-text normal-case"
        />
      </label>
      {error && (
        <span role="alert" data-testid={kbTestIds.timelineError} className="text-xs text-danger">
          {error}
        </span>
      )}
      <div className="flex gap-2">
        <button
          type="button"
          data-testid={kbTestIds.timelineAddSubmit}
          onClick={() => void submit()}
          className="rounded border-0 bg-accent px-2 py-1 text-xs font-medium text-white"
        >
          Add note
        </button>
        <button
          type="button"
          data-testid={kbTestIds.timelineAddCancel}
          onClick={() => {
            onClose();
          }}
          className="rounded border border-border bg-transparent px-2 py-1 text-xs"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
