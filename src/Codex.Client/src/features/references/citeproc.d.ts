// citeproc ships no types. This is the part of its API the bibliography uses.
declare module 'citeproc' {
  interface CiteprocSystem {
    retrieveLocale(lang: string): string;
    retrieveItem(id: string): unknown;
  }

  interface BibliographyParams {
    /** One array of item ids per entry, in the order the entries are returned. */
    entry_ids: string[][];
  }

  interface Engine {
    updateItems(ids: string[]): void;
    makeBibliography(): [BibliographyParams, string[]] | false;
  }

  const CSL: { Engine: new (sys: CiteprocSystem, style: string, lang?: string) => Engine };
  export default CSL;
}
