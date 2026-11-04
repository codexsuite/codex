// Display text for the scene card and the directive cards, from the DTOs only (no clock, no locale).

/** `3860` -> `3,860 words`; `1` -> `1 word`. */
export function wordsLabel(words: number): string {
  const grouped = String(Math.trunc(words)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${grouped} ${words === 1 ? 'word' : 'words'}`;
}

/** The scene's number within its chapter: `3.2` -> `Scene 2`, `2` -> `Scene 2`. */
export function sceneLabel(number: string): string {
  const last = number.split('.').pop() ?? number;
  return `Scene ${last}`;
}

/** A beat's instruction as one line of plain text: links keep their text, emphasis marks go. */
export function plainInstruction(markdown: string): string {
  return markdown
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/(\*\*|__|\*|_|~~)(?=\S)([^*_~]*?\S)\1/g, '$2')
    .replace(/\s+/g, ' ')
    .trim();
}
