import type { ActView } from './manuscriptView';

// What the Write screen shows: the whole book, or one act, chapter or scene.
export type Scope = { kind: 'book' } | { kind: 'act' | 'chapter' | 'scene'; id: string };

export const wholeBook: Scope = { kind: 'book' };

export const scopeValue = (scope: Scope) =>
  scope.kind === 'book' ? 'book' : `${scope.kind}:${scope.id}`;

export function parseScope(value: string): Scope {
  const [kind, id] = value.split(':');
  return (kind === 'act' || kind === 'chapter' || kind === 'scene') && id
    ? { kind, id }
    : wholeBook;
}

/** The acts, chapters and scenes inside the scope. A scope that matches nothing (it was archived) is the book. */
export function scopeActs(acts: readonly ActView[], scope: Scope): ActView[] {
  const narrowed = narrow(acts, scope);
  return narrowed.length === 0 ? [...acts] : narrowed;
}

function narrow(acts: readonly ActView[], scope: Scope): ActView[] {
  switch (scope.kind) {
    case 'book':
      return [...acts];
    case 'act':
      return acts.filter((a) => a.act.id === scope.id);
    case 'chapter':
      return acts.flatMap((a) => {
        const chapters = a.chapters.filter((c) => c.chapter.id === scope.id);
        return chapters.length === 0 ? [] : [{ ...a, chapters }];
      });
    case 'scene':
      return acts.flatMap((a) => {
        const chapters = a.chapters.flatMap((c) => {
          const scenes = c.scenes.filter((s) => s.scene.id === scope.id);
          return scenes.length === 0 ? [] : [{ ...c, scenes }];
        });
        return chapters.length === 0 ? [] : [{ ...a, chapters }];
      });
  }
}

/** Scene and word counts of what is in scope, from the outline's server word counts. */
export function scopeCounts(acts: readonly ActView[]): { scenes: number; words: number } {
  const scenes = acts.flatMap((a) => a.chapters.flatMap((c) => c.scenes));
  return { scenes: scenes.length, words: scenes.reduce((sum, s) => sum + s.scene.words, 0) };
}

/** `634 words · 3 pages · 3m read`: 250 words a page, 238 words a minute (design/screens/write.html). */
export function countsLabel({ words }: { words: number }): string {
  const minutes = Math.round(words / 238);
  const read = minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  const pages = Math.ceil(words / 250);
  return `${words.toLocaleString('en-US')} words · ${pages} ${pages === 1 ? 'page' : 'pages'} · ${read} read`;
}
