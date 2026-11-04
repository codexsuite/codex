import { useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { updateOutlineScene } from '@codex/core/api/outline';
import { structureApi, type LabelRef } from '@codex/core/api/structure';
import { LabelPicker, type PickerLabel } from '../../editing/LabelPicker';
import { toneOf } from '../../editing/labelTone';
import { showToast } from '@codex/core/editing/toastStore';
import { TagIcon } from './icons';
import { writeTestIds } from './testIds';

interface SceneLabelPickerProps {
  sceneId: string;
  labels: LabelRef[];
}

// "Label" in the scene gutter: the book's (and series') labels as checkboxes; a change is saved at once with
// `PUT /api/scenes/{id}/labels` and the outline in the cache carries the answer.
export function SceneLabelPicker({ sceneId, labels }: SceneLabelPickerProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const opener = useRef<HTMLButtonElement>(null);
  const all = useQuery({
    queryKey: ['labels', bookId],
    queryFn: ({ signal }) => structureApi.labels(bookId, { signal }),
    enabled: open,
  });
  const choices: PickerLabel[] = (all.data ?? []).map((l) => ({
    id: l.id,
    name: l.name,
    tone: toneOf(l.color),
  }));

  const change = async (labelIds: string[]) => {
    try {
      const saved = await structureApi.setSceneLabels(sceneId, { labelIds });
      updateOutlineScene(queryClient, bookId, sceneId, (s) => ({ ...s, labels: saved }));
    } catch (error) {
      showToast({
        message: error instanceof Error ? error.message : 'The labels could not be saved.',
      });
    }
  };

  return (
    <span className="relative">
      <button
        ref={opener}
        type="button"
        data-testid={writeTestIds.sceneLabelAdd(sceneId)}
        aria-expanded={open}
        className="write-mini"
        onClick={() => setOpen((o) => !o)}
      >
        <TagIcon />
        Label
      </button>
      {open && (
        <div
          data-testid={writeTestIds.sceneLabelPicker(sceneId)}
          className="write-draft-picker"
          onKeyDown={(e) => {
            if (e.key !== 'Escape') return;
            setOpen(false);
            opener.current?.focus();
          }}
        >
          <LabelPicker
            labels={choices}
            selected={labels.map((l) => l.id)}
            onChange={(ids) => void change(ids)}
          />
        </div>
      )}
    </span>
  );
}
