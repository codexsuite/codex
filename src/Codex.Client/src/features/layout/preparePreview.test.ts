import { describe, expect, it } from 'vitest';
import type { LayoutFigure } from '@codex/core/api/layout';
import { preparePreview } from './preparePreview';
import { scopeCss } from './scopeCss';

const figure: LayoutFigure = {
  figureId: 'f1',
  assetId: 'a1',
  sceneId: 's1',
  filename: 'map.png',
  caption: null,
  placement: 'column',
  widthPx: 480,
  heightPx: 320,
  placedWidthIn: 3,
  placedHeightIn: 2,
  effectiveDpi: 142.2,
  status: 'low',
};

const html =
  '<h2 id="ch-c1">One</h2>' +
  '<div class="codex-scene" data-scene="s1" data-chapter="c1" data-label="Ch 1 · Sc 1"></div>' +
  '<p>First</p><figure id="fig-f1"><img src="x"></figure>' +
  '<div class="scene-break"></div>' +
  '<div class="codex-scene" data-scene="s2" data-chapter="c1" data-label="Ch 1 · Sc 2"></div><p>Second</p>';

describe('preparePreview', () => {
  it('moves each scene marker onto the blocks after it and removes the marker', () => {
    const out = new DOMParser().parseFromString(preparePreview(html, [], []), 'text/html').body;

    expect(out.querySelector('.codex-scene')).toBeNull();
    expect(out.querySelector('h2')!.hasAttribute('data-scene')).toBe(false);
    expect(out.querySelector('p')!.getAttribute('data-label')).toBe('Ch 1 · Sc 1');
    expect(out.querySelectorAll('[data-scene="s2"]')).toHaveLength(1);
    expect(out.querySelector('h2 + p')).not.toBeNull();
  });

  it('flags a low-dpi figure and strips a raw-LaTeX block at the end of its scene', () => {
    const out = new DOMParser().parseFromString(
      preparePreview(
        html,
        [figure],
        [{ kind: 'raw_latex', sceneId: 's1', detail: '\\begin{tabular}', kept: false }],
      ),
      'text/html',
    ).body;

    expect(out.querySelector('[data-testid="layout-dpi-flag"]')!.textContent).toBe('142 dpi');
    const strip = out.querySelector('[data-testid="layout-latex-mark"]')!;
    expect(strip.textContent).toContain('skipped');
    expect(strip.nextElementSibling!.classList.contains('scene-break')).toBe(true);
    expect(strip.previousElementSibling!.tagName).toBe('FIGURE');
  });
});

describe('scopeCss', () => {
  it('keeps the template off the app: body means the page box and every rule sits under the pages', () => {
    const css = scopeCss(
      '/* c */ @page { size: 6in 9in; } body { color: red; } h2 + p, .x { margin: 0 } @media print { p { a: b } }',
    );

    expect(css).toContain('@page{ size: 6in 9in; }');
    expect(css).toContain('.layout-pages .pagedjs_page{ color: red; }');
    expect(css).toContain('.layout-pages h2 + p,.layout-pages .x{');
    expect(css).toContain('@media print{.layout-pages p{ a: b }}');
  });
});
