import type { BookSettings } from '@codex/core/api/settings';
import { BookSection } from './BookSection';
import { SeriesSection } from './SeriesSection';
import { WritingSection } from './WritingSection';
import { useBookDraft } from './useBookDraft';

// The three sections that read the settings response. The draft is seeded once; the page re-mounts it per book.
export function SettingsForm({ settings }: { settings: BookSettings }) {
  const state = useBookDraft(settings);
  return (
    <>
      <BookSection settings={settings} state={state} />
      <SeriesSection settings={settings} />
      <WritingSection settings={settings} state={state} />
    </>
  );
}
