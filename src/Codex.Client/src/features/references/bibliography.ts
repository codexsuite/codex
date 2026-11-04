import CSL from 'citeproc';
import type { SourceCsl } from '@codex/core/api/referenceSources';

export type CitationStyle = 'chicago' | 'apa' | 'harvard';

export const citationStyles: { value: CitationStyle; label: string; file: string }[] = [
  { value: 'chicago', label: 'Chicago', file: 'chicago-author-date.csl' },
  { value: 'apa', label: 'APA', file: 'apa.csl' },
  { value: 'harvard', label: 'Harvard', file: 'harvard-cite-them-right.csl' },
];

export interface BibliographyEntry {
  sourceId: string;
  /** citeproc's HTML for the entry. */
  html: string;
  status: string;
  creditLine: string | null;
}

export const cslFileKey = (file: string) => ['csl-file', file] as const;

/** A style or locale file from `public/csl/`. */
export async function loadCslFile(file: string): Promise<string> {
  const response = await fetch(`/csl/${file}`);
  if (!response.ok) throw new Error(`${file} could not be loaded (${response.status}).`);
  return response.text();
}

/** Formats the cited sources in the style, in the order the style sorts them. citeproc is the only formatter. */
export function formatBibliography(
  sources: SourceCsl[],
  styleXml: string,
  localeXml: string,
): BibliographyEntry[] {
  if (sources.length === 0) return [];
  const items = new Map(sources.map((s) => [s.csl.id, s.csl]));
  const engine = new CSL.Engine(
    { retrieveLocale: () => localeXml, retrieveItem: (id) => items.get(id) },
    styleXml,
  );
  engine.updateItems(sources.map((s) => s.csl.id));
  const made = engine.makeBibliography();
  if (made === false) return [];
  const [params, html] = made;
  return html.map((entry, index) => {
    const cslId = params.entry_ids[index][0];
    const source = sources.find((s) => s.csl.id === cslId);
    if (source === undefined)
      throw new Error(`citeproc returned an entry for unknown item ${cslId}.`);
    return {
      sourceId: source.sourceId,
      html: entry,
      status: source.status,
      creditLine: source.creditLine,
    };
  });
}
