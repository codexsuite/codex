import type { BookSettings, PatchBookSettingsRequest } from '../../api/settings';

/** What the Book and Writing fields hold while the reader types; `targetWords` stays text until it is saved. */
export interface BookDraft {
  title: string;
  authorName: string;
  status: string;
  targetWords: string;
  styleGuide: string;
}

export function draftOf(settings: BookSettings): BookDraft {
  return {
    title: settings.title,
    authorName: settings.authorName ?? '',
    status: settings.status,
    targetWords: settings.targetWords === null ? '' : String(settings.targetWords),
    styleGuide: settings.styleGuide ?? '',
  };
}

/** Only what differs from `saved`. A blank title is never sent; blank target means 0 (clear). */
export function changesOf(draft: BookDraft, saved: BookDraft): PatchBookSettingsRequest {
  const changes: PatchBookSettingsRequest = {};
  if (draft.title.trim() !== '' && draft.title.trim() !== saved.title.trim())
    changes.title = draft.title;
  if (draft.authorName !== saved.authorName) changes.authorName = draft.authorName;
  if (draft.status !== saved.status) changes.status = draft.status;
  if (draft.targetWords !== saved.targetWords) {
    const parsed = Number.parseInt(draft.targetWords, 10);
    changes.targetWords = Number.isNaN(parsed) || parsed < 0 ? 0 : parsed;
  }
  if (draft.styleGuide !== saved.styleGuide) changes.styleGuide = draft.styleGuide;
  return changes;
}

export const hasChanges = (changes: PatchBookSettingsRequest) => Object.keys(changes).length > 0;

export function wordCount(text: string): number {
  const trimmed = text.trim();
  return trimmed === '' ? 0 : trimmed.split(/\s+/).length;
}
