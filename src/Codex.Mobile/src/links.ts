import type { Href } from 'expo-router';

// Typed link builders: the one place a mobile route is written (#35). A screen never builds a path by hand and never
// imports another lane's route file; it calls a builder and passes the result to `router.push` or `<Link href>`.
// Builders return the object form (`pathname` + `params`), which type-checks with and without expo-router's generated
// route types. The route files they point at are listed in CONTRACTS.md.

export const bookTabs = ['plan', 'write', 'knowledge', 'settings'] as const;
export type BookTab = (typeof bookTabs)[number];

export const libraryLink = (): Href => '/';
export const accountLink = (): Href => '/account';
export const speechModelsLink = (): Href => '/speech-models';

/**
 * The book switcher sheet; `bookId` is the book open now (it is marked in the list). To switch: `router.dismiss()`, then
 * `router.replace(bookLink(otherId))`, so Back reaches the Library.
 */
export const bookSwitcherLink = (bookId: string): Href => ({
  pathname: '/book-switcher',
  params: { bookId },
});

/** What a recording is for: a scene (from its sheet) or a Knowledge base entry (from its screen). */
export interface RecordTarget {
  kind: 'scene' | 'entry';
  id: string;
}

/** The `target` route param: `scene:<id>` or `entry:<id>`. */
export const recordTargetParam = (target: RecordTarget): string => `${target.kind}:${target.id}`;

/** Reads the `target` param back; null for anything that is not `scene:<id>` or `entry:<id>`. */
export function parseRecordTarget(param: string | undefined): RecordTarget | null {
  if (param === undefined) return null;
  const colon = param.indexOf(':');
  const kind = param.slice(0, colon);
  const id = param.slice(colon + 1);
  if (colon < 0 || id === '' || (kind !== 'scene' && kind !== 'entry')) return null;
  return { kind, id };
}

/** The recording sheet. Works from any screen; it opens over whatever is showing. */
export const recordLink = (bookId: string, target: RecordTarget): Href => ({
  pathname: '/record',
  params: { bookId, target: recordTargetParam(target) },
});

// Inside a book. The tabs are Plan, Write, Knowledge and Settings; `bookLink` is the book's front door (Plan).
export const bookLink = (bookId: string): Href => planLink(bookId);
export const planLink = (bookId: string): Href => ({
  pathname: '/book/[bookId]/plan',
  params: { bookId },
});
/**
 * Write, optionally opened at a scene or a chapter (`sceneId` / `chapterId` route params; a scene wins when both are
 * given). The same Write screen stays mounted when it is linked to again, so it must react to the params changing.
 */
export const writeLink = (
  bookId: string,
  at: { sceneId?: string; chapterId?: string } = {},
): Href => ({
  pathname: '/book/[bookId]/write',
  params: {
    bookId,
    ...(at.sceneId === undefined ? {} : { sceneId: at.sceneId }),
    ...(at.chapterId === undefined ? {} : { chapterId: at.chapterId }),
  },
});
export const knowledgeLink = (bookId: string): Href => ({
  pathname: '/book/[bookId]/knowledge',
  params: { bookId },
});
export const settingsLink = (bookId: string): Href => ({
  pathname: '/book/[bookId]/settings',
  params: { bookId },
});

export function tabLink(bookId: string, tab: BookTab): Href {
  switch (tab) {
    case 'plan':
      return planLink(bookId);
    case 'write':
      return writeLink(bookId);
    case 'knowledge':
      return knowledgeLink(bookId);
    case 'settings':
      return settingsLink(bookId);
  }
}

/**
 * The scene sheet. It opens over the tab you are on (Plan or Write), not in Plan, so the same link works from any tab.
 */
export const sceneSheetLink = (bookId: string, sceneId: string): Href => ({
  pathname: '/book/[bookId]/scene/[sceneId]',
  params: { bookId, sceneId },
});

/** A Knowledge base entry, pushed on the Knowledge tab. */
export const entryLink = (bookId: string, entryId: string): Href => ({
  pathname: '/book/[bookId]/knowledge/[entryId]',
  params: { bookId, entryId },
});

/** The style guide editor, pushed on the Settings tab. */
export const styleGuideLink = (bookId: string): Href => ({
  pathname: '/book/[bookId]/settings/style-guide',
  params: { bookId },
});

/**
 * The chapter jump sheet (opened from Write's chapter pill), over the current tab. `chapterId` is the chapter being
 * read now (marked in the list). Picking one: `router.dismiss()` the sheet, then `router.navigate(writeLink(bookId, {
 * chapterId }))`.
 */
export const chapterJumpLink = (bookId: string, chapterId?: string): Href => ({
  pathname: '/book/[bookId]/chapters',
  params: chapterId === undefined ? { bookId } : { bookId, chapterId },
});
