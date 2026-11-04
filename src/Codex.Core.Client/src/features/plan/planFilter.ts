import type { OutlineScene } from '../../api/structure';
import { plainSummary } from './format';

/** The toolbar's Find in plan text and Label filter. Scenes that do not match are dimmed, not hidden. */
export interface PlanFilter {
  query: string;
  /** The label a scene must carry, or null for any. */
  labelId: string | null;
}

export const noFilter: PlanFilter = { query: '', labelId: null };

export const isFiltering = (filter: PlanFilter) =>
  filter.query.trim() !== '' || filter.labelId !== null;

/** Whether the scene passes the filter: its summary or title contains the text, and it carries the label. */
export function matchesFilter(scene: OutlineScene, filter: PlanFilter): boolean {
  const query = filter.query.trim().toLowerCase();
  const text = `${plainSummary(scene.summary)} ${scene.title ?? ''}`.toLowerCase();
  return (
    (query === '' || text.includes(query)) &&
    (filter.labelId === null || scene.labels.some((l) => l.id === filter.labelId))
  );
}
