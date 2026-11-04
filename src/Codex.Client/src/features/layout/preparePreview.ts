import type { LayoutFigure, LayoutFlag } from '@codex/core/api/layout';
import { layoutTestIds } from './testIds';

export const SCENE_ATTR = 'data-scene';
export const LABEL_ATTR = 'data-label';

// Turns the server's Quick preview fragment into what Paged.js flows: each scene's blocks carry data-scene and
// data-label (the server's empty div.codex-scene markers say where a scene starts, and are removed so the template's
// `h2 + p` rules still match), a low-dpi figure gets a warn flag, and a skipped raw-LaTeX block a hatched strip at the
// end of its scene. The marks are UI only (`.codex-mark`, never printed) and the Print issues toggle hides them.
export function preparePreview(
  html: string,
  figures: readonly LayoutFigure[],
  flags: readonly LayoutFlag[],
): string {
  const doc = new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html');
  const root = doc.body;
  const low = new Map(
    figures.filter((f) => f.status === 'low').map((f) => [`fig-${f.figureId}`, f]),
  );
  const strips = new Map<string, LayoutFlag[]>();
  for (const flag of flags) {
    if (flag.kind !== 'raw_latex' || flag.sceneId === null) continue;
    strips.set(flag.sceneId, [...(strips.get(flag.sceneId) ?? []), flag]);
  }

  let scene: { id: string; label: string } | null = null;
  const closeScene = (before: Element | null) => {
    if (scene === null) return;
    // The break between two scenes is not part of the first one.
    const last = before === null ? root.lastElementChild : before.previousElementSibling;
    const anchor = last?.classList.contains('scene-break') ? last : before;
    for (const flag of strips.get(scene.id) ?? []) {
      const strip = doc.createElement('div');
      strip.className = 'codex-mark codex-mark-latex';
      strip.setAttribute('data-testid', layoutTestIds.latexMark);
      strip.setAttribute(SCENE_ATTR, scene.id);
      strip.setAttribute(LABEL_ATTR, scene.label);
      strip.setAttribute('data-latex', flag.detail);
      strip.textContent = `Raw LaTeX${flag.kept ? '' : ' skipped'}: ${flag.detail}`;
      root.insertBefore(strip, anchor);
    }
  };

  for (const child of Array.from(root.children)) {
    if (child.classList.contains('codex-scene')) {
      closeScene(child);
      scene = {
        id: child.getAttribute(SCENE_ATTR) ?? '',
        label: child.getAttribute(LABEL_ATTR) ?? '',
      };
      child.remove();
      continue;
    }
    if (scene === null || child.classList.contains('codex-mark')) continue;
    if (/^H[12]$/.test(child.tagName) && child.id.startsWith('ch-')) {
      // A chapter heading before the first scene marker belongs to no scene.
      continue;
    }
    child.setAttribute(SCENE_ATTR, scene.id);
    child.setAttribute(LABEL_ATTR, scene.label);
  }
  closeScene(null);

  for (const [id, figure] of low) {
    const target = root.querySelector(`[id="${id}"]`);
    if (target === null) continue;
    const flag = doc.createElement('span');
    flag.className = 'codex-mark codex-mark-dpi';
    flag.setAttribute('data-testid', layoutTestIds.dpiFlag);
    flag.textContent = `${Math.round(figure.effectiveDpi ?? 0)} dpi`;
    target.prepend(flag);
  }
  return root.innerHTML;
}
