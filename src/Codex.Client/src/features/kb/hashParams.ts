// Edits one key of the current URL hash and keeps the others (`#scene=5.2&kb=x` -> set `kb`). Returns the new hash
// (with the leading `#`, or '' when nothing is left). A bare hash key (`#new`) is kept as it is.
export function withHashKey(hash: string, key: string, value: string | null): string {
  const kept = hash
    .replace(/^#/, '')
    .split('&')
    .filter((part) => part !== '' && part.split('=')[0] !== key);
  if (value !== null) kept.push(`${key}=${encodeURIComponent(value)}`);
  return kept.length === 0 ? '' : `#${kept.join('&')}`;
}
