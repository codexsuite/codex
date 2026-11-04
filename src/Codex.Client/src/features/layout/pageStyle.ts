import { scopeCss } from './scopeCss';

// Page sizes by trim slug (the server's TrimSizes), for the @page rule that makes the trim picker re-flow the pages.
export const PAGE_SIZE: Record<string, string> = {
  '6x9': '6in 9in',
  '5.5x8.5': '5.5in 8.5in',
  'us-letter': '8.5in 11in',
  a4: '210mm 297mm',
};

// The neutral look of a template without preview.css (the banner points the writer to Proof).
const HOUSE_STYLE = `
body { background: #fff; color: #1a1a1a; font-family: Georgia, serif; font-size: 11pt; line-height: 1.4; }
@page { margin: 0.75in; }
h1, h2 { text-align: center; font-weight: normal; break-before: page; }
p { margin: 0 0 0.6em; }
.scene-break::before { content: "*   *   *"; display: block; text-align: center; margin: 1em 0; }
.sidebar, .aside, .spell, .statblock { border: 1px solid #1a1a1a; padding: 0.5em; margin: 1em 0; }
.pagebreak { break-after: page; }
figure img { width: 100%; }
.missing-figure { border: 1px dashed #1a1a1a; padding: 1em; text-align: center; font-style: italic; }
`;

export function pageCss(css: string | null, trim: string): string {
  const size = PAGE_SIZE[trim];
  return scopeCss(css ?? HOUSE_STYLE) + (size ? `@page { size: ${size}; }` : '');
}
