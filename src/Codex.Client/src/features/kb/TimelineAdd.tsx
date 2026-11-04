import { useState } from 'react';
import { TimelineAddForm } from './TimelineAddForm';
import { kbTestIds } from './testIds';

interface TimelineAddProps {
  bookId: string;
  onAdd: (sceneId: string, sceneNumber: string, text: string) => Promise<void>;
}

// "Add a note at…": the button, which opens the form.
export function TimelineAdd({ bookId, onAdd }: TimelineAddProps) {
  const [open, setOpen] = useState(false);
  if (open) return <TimelineAddForm bookId={bookId} onAdd={onAdd} onClose={() => setOpen(false)} />;
  return (
    <div className="px-4 py-2">
      <button
        type="button"
        data-testid={kbTestIds.timelineAdd}
        onClick={() => setOpen(true)}
        className="rounded border border-border bg-transparent px-2 py-1 text-xs font-medium text-accent"
      >
        Add a note at…
      </button>
    </div>
  );
}
