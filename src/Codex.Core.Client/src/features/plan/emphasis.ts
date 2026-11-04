import type { OutlineScene } from '../../api/structure';
import { isFiltering, matchesFilter, noFilter, type PlanFilter } from './planFilter';

/** How a card or row reads while an entry is highlighted or the toolbar filters: matches stand out, the rest dim. */
export type Emphasis = 'none' | 'hit' | 'dim';

export function emphasisOf(
  scene: OutlineScene,
  highlightedEntry: string | null,
  filter: PlanFilter = noFilter,
): Emphasis {
  if (highlightedEntry === null && !isFiltering(filter)) return 'none';
  const linked =
    highlightedEntry === null || scene.linkedEntries.some((e) => e.id === highlightedEntry);
  if (!linked || !matchesFilter(scene, filter)) return 'dim';
  return highlightedEntry === null ? 'none' : 'hit';
}
