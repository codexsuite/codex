// Paged.js puts a template's preview.css into the document, where `body` and bare element rules would restyle the
// app. Every qualified rule is prefixed with the pages' container class instead; `html`, `body` and `:root` mean the
// page box. @page rules and the at-rules that are not conditional (@font-face, @keyframes) are kept as written.
export const PAGES_CLASS = 'layout-pages';
const PAGE_BOX = `.${PAGES_CLASS} .pagedjs_page`;
const ROOT_SELECTORS = new Set(['html', 'body', ':root']);

export function scopeCss(css: string): string {
  return scopeRules(css.replace(/\/\*[\s\S]*?\*\//g, ''));
}

function scopeRules(css: string): string {
  let out = '';
  let rest = css;
  while (rest.trim() !== '') {
    const open = rest.indexOf('{');
    if (open < 0) break;
    const prelude = rest.slice(0, open).trim();
    const close = matchingBrace(rest, open);
    const body = rest.slice(open + 1, close);
    rest = rest.slice(close + 1);
    if (/^@(media|supports|layer)\b/i.test(prelude)) {
      out += `${prelude}{${scopeRules(body)}}`;
    } else if (prelude.startsWith('@')) {
      out += `${prelude}{${body}}`;
    } else {
      out += `${prelude.split(',').map(scopeSelector).join(',')}{${body}}`;
    }
  }
  return out;
}

function scopeSelector(selector: string): string {
  const trimmed = selector.trim();
  const [head, ...tail] = trimmed.split(/(?=[\s>+~])/);
  if (ROOT_SELECTORS.has(head)) return PAGE_BOX + tail.join('');
  return `.${PAGES_CLASS} ${trimmed}`;
}

function matchingBrace(text: string, open: number): number {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === '{') depth++;
    if (text[i] === '}' && --depth === 0) return i;
  }
  return text.length;
}
