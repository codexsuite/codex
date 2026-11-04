import { useState } from 'react';
import type { OutlineScene } from '@codex/core/api/structure';
import { SceneLabels } from './SceneLabels';
import { TagIcon } from './icons';
import { miniButton } from './miniButton';
import { planTestIds } from './testIds';

// `Label` on a card: the book's (and series') labels as checkboxes, saved at once. The same picker as the ⋯ menu's Labels.
export function SceneLabelAdd({ scene }: { scene: OutlineScene }) {
  const [open, setOpen] = useState(false);
  return (
    <span className="relative">
      <button
        type="button"
        data-testid={planTestIds.sceneLabelAdd(scene.id)}
        aria-expanded={open}
        className={miniButton}
        onClick={() => setOpen((o) => !o)}
      >
        <TagIcon />
        Label
      </button>
      {open && (
        <div
          data-testid={planTestIds.sceneLabelPicker(scene.id)}
          className="absolute top-full left-0 z-20 mt-1 min-w-44 rounded-md border border-border bg-surface p-1 shadow-raised"
          onKeyDown={(e) => {
            if (e.key === 'Escape') setOpen(false);
          }}
        >
          <SceneLabels scene={scene} />
        </div>
      )}
    </span>
  );
}
