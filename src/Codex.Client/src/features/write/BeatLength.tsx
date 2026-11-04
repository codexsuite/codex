import { useState } from 'react';
import { PenIcon } from './icons';
import { writeTestIds } from './testIds';

const PRESETS = [200, 400, 600];

interface BeatLengthProps {
  beatId: string;
  words: number | null;
  onChange: (words: number) => void;
}

// Target length: a 200 | 400 | 600 | ✎ segmented control; ✎ takes a custom number and then shows it.
export function BeatLength({ beatId, words, onChange }: BeatLengthProps) {
  const [editing, setEditing] = useState(false);
  const custom = words !== null && !PRESETS.includes(words);
  return (
    <>
      <div role="group" aria-label="Target length" className="write-seg">
        {PRESETS.map((n) => (
          <button
            key={n}
            type="button"
            data-testid={writeTestIds.beatLength(beatId, n)}
            aria-pressed={words === n}
            onClick={() => onChange(n)}
          >
            {n}
          </button>
        ))}
        {editing ? (
          <input
            autoFocus
            type="number"
            min={1}
            aria-label="Custom length in words"
            data-testid={writeTestIds.beatCustomLength(beatId)}
            defaultValue={custom ? (words ?? undefined) : undefined}
            onBlur={() => setEditing(false)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') setEditing(false);
              if (e.key !== 'Enter') return;
              const value = Number(e.currentTarget.value);
              if (Number.isInteger(value) && value > 0) onChange(value);
              setEditing(false);
            }}
          />
        ) : (
          <button
            type="button"
            data-testid={writeTestIds.beatCustomLength(beatId)}
            aria-pressed={custom}
            aria-label={custom ? undefined : 'Custom length'}
            title="Custom length"
            onClick={() => setEditing(true)}
          >
            {custom ? words : <PenIcon />}
          </button>
        )}
      </div>
      <span className="text-xs text-muted">words</span>
    </>
  );
}
