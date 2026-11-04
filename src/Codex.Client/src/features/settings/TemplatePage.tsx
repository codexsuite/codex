import { settingsTestIds } from './testIds';

// A template's look drawn to scale: the server's html5 (a package's sample.md, or a Quick preview of the book) with the
// template's preview.css, laid out at the page's width and margins inside an iframe, then scaled down. The frame is a
// sandbox with no scripts and a Content-Security-Policy that loads nothing from the network, so a user template's CSS
// and HTML stay inside it and never restyle or reach the app. The page itself stays paper-coloured in both themes (it
// is the book's artwork, not UI).

export interface PageGeometry {
  widthIn: number;
  heightIn: number;
  marginTopIn: number;
  marginBottomIn: number;
  marginInnerIn: number;
  marginOuterIn: number;
}

// Page sizes by trim slug (the server's TrimSizes), with the house margins, for a preview whose template sent none.
const TRIMS: Record<string, [number, number]> = {
  '6x9': [6, 9],
  '5.5x8.5': [5.5, 8.5],
  'us-letter': [8.5, 11],
  '8.5x11': [8.5, 11],
  a4: [8.27, 11.69],
};

export function trimGeometry(trim: string): PageGeometry {
  const [widthIn, heightIn] = TRIMS[trim] ?? [6, 9];
  const margin = 0.75;
  return {
    widthIn,
    heightIn,
    marginTopIn: margin,
    marginBottomIn: margin,
    marginInnerIn: margin,
    marginOuterIn: margin,
  };
}

const HOUSE_STYLE =
  'body{background:#fff;color:#1a1a1a;font-family:Georgia,serif;font-size:11pt;line-height:1.4}' +
  'h1,h2{text-align:center;font-weight:normal}';

const PX_PER_IN = 96;

function pageDocument(
  html: string,
  css: string | null,
  page: PageGeometry,
  scroll: boolean,
): string {
  const frame =
    `html,body{margin:0}body{box-sizing:border-box;width:${page.widthIn}in;` +
    `${scroll ? '' : `height:${page.heightIn}in;overflow:hidden;`}` +
    `padding:${page.marginTopIn}in ${page.marginOuterIn}in ${page.marginBottomIn}in ${page.marginInnerIn}in}`;
  return (
    '<!doctype html><html><head><meta charset="utf-8">' +
    `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:">` +
    `<style>${frame}</style><style>${css ?? HOUSE_STYLE}</style>` +
    `</head><body><div class="codex-body">${html}</div></body></html>`
  );
}

interface TemplatePageProps {
  html: string;
  css: string | null;
  page: PageGeometry;
  /** Width the page is drawn at, in CSS pixels. */
  width: number;
  /** A viewport this tall (CSS pixels) that scrolls through the whole text; without it, only the first page shows. */
  scrollHeight?: number;
  title: string;
  testId?: string;
}

export function TemplatePage({
  html,
  css,
  page,
  width,
  scrollHeight,
  title,
  testId,
}: TemplatePageProps) {
  const pageWidth = page.widthIn * PX_PER_IN;
  const scale = width / pageWidth;
  const frameHeight = scrollHeight ? scrollHeight / scale : page.heightIn * PX_PER_IN;
  return (
    <div
      data-testid={testId}
      className="relative shrink-0 overflow-hidden rounded-sm bg-white shadow-sm ring-1 ring-black/10"
      style={{ width, height: frameHeight * scale }}
    >
      <iframe
        title={title}
        data-testid={testId && settingsTestIds.pageFrame(testId)}
        sandbox=""
        srcDoc={pageDocument(html, css, page, scrollHeight !== undefined)}
        tabIndex={scrollHeight ? 0 : -1}
        aria-hidden={scrollHeight ? undefined : true}
        className={`absolute top-0 left-0 origin-top-left border-0 ${scrollHeight ? '' : 'pointer-events-none'}`}
        style={{ width: pageWidth, height: frameHeight, transform: `scale(${scale})` }}
      />
    </div>
  );
}
