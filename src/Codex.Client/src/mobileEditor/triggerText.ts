/**
 * `/` and `@` open their menus only after whitespace or at the start of a line, so a trigger typed right after a word
 * needs a space in front of it.
 */
export function triggerText(characterBefore: string, trigger: '/' | '@'): string {
  const needsSpace = characterBefore !== '' && !/\s/.test(characterBefore);
  return needsSpace ? ` ${trigger}` : trigger;
}
