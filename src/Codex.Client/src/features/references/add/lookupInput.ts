import type { LookupKind } from '@codex/core/api/referenceLookup';

export interface LookupInput {
  kind: LookupKind;
  value: string;
}

/** Tells what was pasted: a web address, a DOI (bare, `doi:` or a doi.org link) or an ISBN. Null when it is none of them. */
export function detectLookup(text: string): LookupInput | null {
  const value = text.trim();
  if (
    /^https?:\/\/(dx\.)?doi\.org\/10\./i.test(value) ||
    /^(doi:\s*)?10\.\d{4,9}\/\S+$/i.test(value)
  ) {
    return { kind: 'doi', value };
  }
  if (/^https?:\/\/\S+$/i.test(value)) return { kind: 'url', value };
  const digits = value.replace(/^isbn[:\s]*/i, '').replace(/[-\s]/g, '');
  if (/^(\d{13}|\d{9}[\dXx])$/.test(digits)) return { kind: 'isbn', value };
  return null;
}
