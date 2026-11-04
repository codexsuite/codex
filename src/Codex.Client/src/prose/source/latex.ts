import { StreamLanguage, HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { stex } from '@codemirror/legacy-modes/mode/stex';
import { tags } from '@lezer/highlight';

/** LaTeX for CodeMirror (the `stex` mode). Used for `{=latex}` fences here, and by the rich view's raw LaTeX block. */
export const latexLanguage = StreamLanguage.define(stex);

// Token colours come from the design tokens, so both themes follow `data-theme`.
export const latexHighlightStyle = HighlightStyle.define([
  { tag: tags.keyword, color: 'var(--color-accent)' },
  { tag: tags.tagName, color: 'var(--category-story)' },
  { tag: tags.atom, color: 'var(--category-research)' },
  { tag: [tags.bracket, tags.operator], color: 'var(--color-muted)' },
  { tag: tags.comment, color: 'var(--color-muted)', fontStyle: 'italic' },
  { tag: tags.string, color: 'var(--category-blog)' },
  { tag: tags.number, color: 'var(--category-person)' },
  { tag: tags.invalid, color: 'var(--color-danger)' },
]);

/** The extension a CodeMirror editor adds to highlight LaTeX: the language plus its token colours. */
export const latexHighlighting = [latexLanguage, syntaxHighlighting(latexHighlightStyle)];
