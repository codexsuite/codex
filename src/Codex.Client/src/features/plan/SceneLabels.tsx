import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { LabelPicker, type PickerLabel } from '../../editing/LabelPicker';
import { showToast } from '@codex/core/editing/toastStore';
import { structureApi, type Label, type OutlineScene } from '@codex/core/api/structure';
import { toneOf } from '../../editing/labelTone';
import { updateOutlineScene } from '@codex/core/api/outline';

/** The default colour of a label made from the picker. */
const NEW_LABEL_COLOR = 'info';

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

const labelsKey = (bookId: string) => ['labels', bookId] as const;

// Pick a scene's labels from the book's (and series') labels, or make a new one. A label whose colour is outside the
// four tones is offered with a hollow dot, as the card draws it.
export function SceneLabels({ scene }: { scene: OutlineScene }) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const labels = useQuery({
    queryKey: labelsKey(bookId),
    queryFn: ({ signal }) => structureApi.labels(bookId, { signal }),
  });

  if (labels.isError)
    return <p className="m-0 px-2 py-1 text-sm text-danger">{labels.error.message}</p>;
  if (!labels.data) return <p className="m-0 px-2 py-1 text-sm text-muted">Loading labels…</p>;

  const offered: PickerLabel[] = labels.data.map((l: Label) => ({
    id: l.id,
    name: l.name,
    tone: toneOf(l.color),
  }));

  const save = async (picked: string[]) => {
    try {
      const saved = await structureApi.setSceneLabels(scene.id, { labelIds: picked });
      updateOutlineScene(queryClient, bookId, scene.id, (s) => ({ ...s, labels: saved }));
    } catch (error) {
      showToast({ message: `Could not save the labels: ${reasonOf(error)}` });
    }
  };

  const create = async (name: string) => {
    try {
      const created = await structureApi.createLabel(bookId, { name, color: NEW_LABEL_COLOR });
      await queryClient.invalidateQueries({ queryKey: labelsKey(bookId) });
      await save([...scene.labels.map((l) => l.id), created.id]);
    } catch (error) {
      showToast({ message: `Could not create the label: ${reasonOf(error)}` });
    }
  };

  return (
    <LabelPicker
      labels={offered}
      selected={scene.labels.map((l) => l.id)}
      onChange={(picked) => void save(picked)}
      onCreate={(name) => void create(name)}
    />
  );
}
