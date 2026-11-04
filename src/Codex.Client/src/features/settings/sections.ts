import type { SettingsSection } from '@codex/core/shell/deepLink';
import { peoplePath } from './accessIcons';

export interface NavSection {
  section: SettingsSection;
  label: string;
  /** Path data for the 14px line icon (16×16 viewBox, stroked). */
  icon: string;
}

/** The seven section cards, in page order. `rules` and `letters` are anchors inside Citations. */
export const navSections: NavSection[] = [
  { section: 'book', label: 'Book', icon: 'M3 2.5h8.5v11H3zM5.5 2.5v11' },
  {
    section: 'series',
    label: 'Series',
    icon: 'M2.5 3.5h3v10h-3zM6.5 3.5h3v10h-3zM10.5 4l2.6-.7 2.4 9.7-2.6.7z',
  },
  { section: 'writing', label: 'Writing', icon: 'M10.5 2.5 13.5 5.5 6 13H3v-3z' },
  {
    section: 'citations',
    label: 'Citations & permissions',
    icon: 'M3 4.5h4v4H4.5L3 11zM9 4.5h4v4h-2.5L9 11z',
  },
  {
    section: 'layout',
    label: 'Layout & print',
    icon: 'M4.5 6V2.5h7V6M4.5 11.5h-2v-5h11v5h-2M4.5 9.5h7v4h-7z',
  },
  { section: 'sharing', label: 'Sharing', icon: peoplePath },
  {
    section: 'archive',
    label: 'Archive & export',
    icon: 'M2.5 3h11v3h-11zM3.5 6v7.5h9V6M6.5 8.5h3',
  },
];
