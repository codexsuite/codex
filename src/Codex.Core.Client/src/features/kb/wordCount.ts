// Words in an entry's Content, counted on what the author reads: the Markdown marks (heading `#`, quote `>`, list
// bullets) and the target of a `[Name](kb:id)` mention are not words; the mention's name is.
export function wordCount(markdown: string): number {
  const text = markdown
    .replace(/\]\([^)]*\)/g, ']')
    .replace(/^\s*(?:#{1,6}|>|[-*+]|\d+\.)\s+/gm, '')
    .replace(/[[\]*_`]/g, ' ');
  return text.split(/\s+/).filter((w) => /[\p{L}\p{N}]/u.test(w)).length;
}
