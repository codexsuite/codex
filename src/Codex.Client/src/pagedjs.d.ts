// pagedjs ships no types; this is the part Quick preview uses.
declare module 'pagedjs' {
  export interface PagedFlow {
    total: number;
  }

  export class Previewer {
    polisher: { destroy(): void };

    preview(
      content: string | DocumentFragment,
      stylesheets: Array<Record<string, string> | string>,
      renderTo: HTMLElement,
    ): Promise<PagedFlow>;
  }
}
