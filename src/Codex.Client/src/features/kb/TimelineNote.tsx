import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { KbTimelineNote } from '@codex/core/api/kbEntries';
import { writeLink } from '@codex/core/shell/links';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface TimelineNoteProps {
  note: KbTimelineNote;
  /** The number of the entry's first use when this note sits after it (rule R3); a warning, nothing is blocked. */
  lateAfter?: string;
  onSave: (text: string) => Promise<void>;
}

// One note in the timeline: its scene, Open in Write, and the text, which Edit turns into a field to change in place.
export function TimelineNote({ note, lateAfter, onSave }: TimelineNoteProps) {
  const [text, setText] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const canWrite = useCanWrite();

  const save = async () => {
    try {
      await onSave(text ?? '');
      setText(null);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That change could not be saved.');
    }
  };

  return (
    <li data-testid={kbTestIds.timelineNote(note.id)} className="border-b border-border px-4 py-2">
      <div className="flex items-center gap-2">
        <span className="flex-1 text-xs font-medium text-muted">From scene {note.sceneNumber}</span>
        {canWrite && text === null && (
          <button
            type="button"
            data-testid={kbTestIds.timelineEdit(note.id)}
            onClick={() => setText(note.body)}
            className="border-0 bg-transparent p-0 text-xs font-medium text-accent hover:underline"
          >
            Edit
          </button>
        )}
        <Link
          to={writeLink(note.bookId, { scene: note.sceneNumber })}
          className="text-xs font-medium text-accent no-underline hover:underline"
        >
          Open in Write
        </Link>
      </div>
      {lateAfter !== undefined && (
        <p data-testid={kbTestIds.timelineWarning(note.id)} className="m-0 mt-1 text-xs text-warn">
          This note comes after the entry&rsquo;s first use in scene {lateAfter}.
        </p>
      )}
      {text === null ? (
        <p className="m-0 mt-1 text-sm">{note.body}</p>
      ) : (
        <div className="mt-1 flex flex-col gap-1">
          <textarea
            data-testid={kbTestIds.timelineEditText(note.id)}
            aria-label="Note text"
            rows={2}
            value={text}
            onChange={(event) => setText(event.target.value)}
            className="rounded border border-border bg-bg p-2 text-sm text-text"
          />
          {error && (
            <span
              role="alert"
              data-testid={kbTestIds.timelineError}
              className="text-xs text-danger"
            >
              {error}
            </span>
          )}
          <div className="flex gap-2">
            <button
              type="button"
              data-testid={kbTestIds.timelineEditSave(note.id)}
              onClick={() => void save()}
              className="rounded border-0 bg-accent px-2 py-1 text-xs font-medium text-white"
            >
              Save
            </button>
            <button
              type="button"
              data-testid={kbTestIds.timelineEditCancel(note.id)}
              onClick={() => {
                setText(null);
                setError(null);
              }}
              className="rounded border border-border bg-transparent px-2 py-1 text-xs"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </li>
  );
}
