import { useState } from 'react';
import type { Kb } from '@codex/core/api/kb';
import { buildKbSidebar } from '@codex/core/features/kb/kbSidebar';
import { KbSidebarFrame } from '../shell/KbSidebarFrame';

// The sidebar frame over a fixed Knowledge base, with its search and category filter held in state the way
// BookWorkspace holds them.
export function KbSidebarHarness({ kb }: { kb: Kb }) {
  const [query, setQuery] = useState('');
  const [hidden, setHidden] = useState<string[]>([]);
  const model = buildKbSidebar(kb, query, hidden);
  return (
    <KbSidebarFrame
      sections={model.sections}
      totalEntries={model.totalEntries}
      categories={model.categories}
      query={query}
      onQueryChange={setQuery}
      hiddenCategories={hidden}
      onHiddenCategoriesChange={setHidden}
    />
  );
}
