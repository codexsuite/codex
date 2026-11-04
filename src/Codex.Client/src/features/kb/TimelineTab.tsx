import type { KbTimelineNote, KbUse } from '@codex/core/api/kbEntries';
import { TimelineAdd } from './TimelineAdd';
import { TimelineNote } from './TimelineNote';
import { kbTestIds } from './testIds';
import { lateNotes } from '@codex/core/features/kb/timelineOrder';
import { useTimelineNotes } from './useTimelineNotes';
import { useCanWrite } from '../../shell/bookAccess';

interface TimelineTabProps {
  bookId: string;
  entryId: string;
  notes: KbTimelineNote[];
  /** The entry's uses in book order; a note behind the first one gets a warning (rule R3). */
  usedIn?: KbUse[];
}

// Facts that hold from a point in the book onward (Write's Codex additions), in book order after "Start of book".
// Notes can be edited in place and added at any scene.
export function TimelineTab({ bookId, entryId, notes, usedIn = [] }: TimelineTabProps) {
  const timeline = useTimelineNotes(bookId, entryId);
  const late = lateNotes(usedIn, notes);
  // Below write (#80) the timeline reads: no Add, no Edit.
  const canWrite = useCanWrite();
  return (
    <div>
      <ol className="m-0 list-none p-0">
        <li className="border-b border-border px-4 py-2 text-xs font-medium text-muted">
          Start of book
        </li>
        {notes.length === 0 && (
          <li data-testid={kbTestIds.timelineEmpty} className="px-4 py-3 text-sm text-muted">
            No timeline notes yet.
          </li>
        )}
        {notes.map((note) => (
          <TimelineNote
            key={note.id}
            note={note}
            lateAfter={late.get(note.id)}
            onSave={(text) => timeline.edit(note.id, note.body, text)}
          />
        ))}
      </ol>
      {canWrite && <TimelineAdd bookId={bookId} onAdd={timeline.add} />}
    </div>
  );
}
