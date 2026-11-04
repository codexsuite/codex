import type { Node as PmNode } from '@tiptap/pm/model';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Marker } from '@codex/core/api/references';
import { markerTone } from './markerTone';
import { writeTestIds } from './testIds';

export const markersKey = new PluginKey('writeMarkers');

export interface MarkersSource {
  byUse: ReadonlyMap<string, Marker>;
  open: (marker: Marker, anchor: HTMLElement) => void;
}

function markerAttrs(marker: Marker) {
  return {
    'data-testid': writeTestIds.marker(marker.useId),
    'data-marker-use': marker.useId,
    'data-source': marker.sourceId,
    'data-number': String(marker.number),
    'data-status': marker.status,
    'data-tone': markerTone(marker.status),
  };
}

function badge(marker: Marker, open: MarkersSource['open']): HTMLElement {
  const sup = document.createElement('sup');
  for (const [name, value] of Object.entries(markerAttrs(marker))) sup.setAttribute(name, value);
  sup.className = 'write-marker';
  sup.contentEditable = 'false';
  sup.tabIndex = 0;
  sup.setAttribute('role', 'button');
  sup.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    open(marker, sup);
  });
  sup.textContent = String(marker.number);
  sup.addEventListener('click', (event) => {
    event.preventDefault();
    open(marker, sup);
  });
  return sup;
}

interface Run {
  from: number;
  to: number;
  marker: Marker;
}

function markerDecorations(doc: PmNode, source: MarkersSource): Decoration[] {
  const runs: Run[] = [];
  const decorations: Decoration[] = [];
  doc.descendants((node, pos) => {
    if (node.type.name === 'refMarker') {
      const marker = source.byUse.get(node.attrs.id as string);
      if (marker) {
        decorations.push(
          Decoration.node(pos, pos + node.nodeSize, {
            ...markerAttrs(marker),
            class: 'write-marker write-marker-bare',
          }),
        );
      }
      return false;
    }
    if (!node.isText) return true;
    for (const mark of node.marks) {
      if (mark.type.name !== 'ref') continue;
      const marker = source.byUse.get(mark.attrs.id as string);
      if (!marker) continue;
      const open = runs.find((r) => r.marker === marker && r.to === pos);
      if (open) open.to = pos + node.nodeSize;
      else runs.push({ from: pos, to: pos + node.nodeSize, marker });
    }
    return true;
  });
  for (const { from, to, marker } of runs) {
    decorations.push(
      Decoration.inline(from, to, { class: 'write-ref', 'data-tone': markerTone(marker.status) }),
      Decoration.widget(to, () => badge(marker, source.open), {
        key: `marker-${marker.useId}-${marker.number}-${marker.status}`,
        side: 1,
        stopEvent: () => true,
      }),
    );
  }
  return decorations;
}

/**
 * Draws the book's source markers in the prose: the words a `ref:` mark covers take the status tone, and a
 * superscript number follows them (a bare `[^]` marker shows its number in place). Clicking a number opens the
 * marker card. `source` is read on every redraw, so new markers only need a transaction (meta `redraw`).
 */
export function markersPlugin(source: () => MarkersSource): Plugin {
  return new Plugin({
    key: markersKey,
    props: {
      decorations: (state) =>
        DecorationSet.create(state.doc, markerDecorations(state.doc, source())),
      handleClick(_view, _pos, event) {
        if (!(event.target instanceof Element)) return false;
        const hit = event.target.closest<HTMLElement>('[data-marker-use]');
        const marker = hit?.dataset.markerUse
          ? source().byUse.get(hit.dataset.markerUse)
          : undefined;
        if (!hit || !marker) return false;
        source().open(marker, hit);
        return true;
      },
    },
  });
}
