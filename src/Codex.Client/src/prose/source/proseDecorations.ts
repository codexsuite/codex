import { RangeSetBuilder, type Extension } from '@codemirror/state';
import {
  Decoration,
  EditorView,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate,
} from '@codemirror/view';

// The Codex-specific syntax that Markdown does not know, drawn over the text with mark decorations only, so every
// character stays editable and the stored text is untouched:
//   ::name{...}         a directive line          (cm-directive)
//   :::name / :::       a container's fence lines  (cm-container)
//   `...`{=latex}       an inline raw LaTeX span   (cm-inline-latex)
//   [Name](kb:<id>)     a mention: the name reads normally, the id is dimmed (cm-mention, cm-id)
//   [text](ref:<id>) and [Title](chapter:<id>) likewise (cm-ref, cm-chapter)

const DIRECTIVE = /^::[A-Za-z][\w-]*.*$/;
const CONTAINER = /^:::.*$/;
const INLINE_LATEX = /`[^`\n]+`\{=latex\}/g;
const LINK = /\[((?:[^\][\\]|\\.)*)\]\(((?:kb|ref|chapter):[^)\s]+)\)/g;

const LINK_CLASS: Record<string, string> = {
  kb: 'cm-mention',
  ref: 'cm-ref',
  chapter: 'cm-chapter',
};

interface Mark {
  from: number;
  to: number;
  className: string;
}

/** The marks for one line of text starting at `offset`. */
export function lineMarks(text: string, offset: number): Mark[] {
  const marks: Mark[] = [];
  if (CONTAINER.test(text)) {
    marks.push({ from: offset, to: offset + text.length, className: 'cm-container' });
    return marks;
  }
  if (DIRECTIVE.test(text)) {
    marks.push({ from: offset, to: offset + text.length, className: 'cm-directive' });
    return marks;
  }
  for (const m of text.matchAll(INLINE_LATEX)) {
    marks.push({
      from: offset + m.index,
      to: offset + m.index + m[0].length,
      className: 'cm-inline-latex',
    });
  }
  for (const m of text.matchAll(LINK)) {
    const scheme = m[2].slice(0, m[2].indexOf(':'));
    const start = offset + m.index;
    const labelEnd = start + 1 + m[1].length + 1;
    marks.push({ from: start, to: labelEnd, className: LINK_CLASS[scheme] });
    marks.push({ from: labelEnd, to: start + m[0].length, className: 'cm-id' });
  }
  return marks.sort((a, b) => a.from - b.from || a.to - b.to);
}

function decorate(view: EditorView): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  for (const { from, to } of view.visibleRanges) {
    let pos = from;
    while (pos <= to) {
      const line = view.state.doc.lineAt(pos);
      for (const mark of lineMarks(line.text, line.from)) {
        builder.add(mark.from, mark.to, Decoration.mark({ class: mark.className }));
      }
      pos = line.to + 1;
    }
  }
  return builder.finish();
}

export const proseDecorations: Extension = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;
    constructor(view: EditorView) {
      this.decorations = decorate(view);
    }
    update(update: ViewUpdate) {
      if (update.docChanged || update.viewportChanged) this.decorations = decorate(update.view);
    }
  },
  { decorations: (plugin) => plugin.decorations },
);
