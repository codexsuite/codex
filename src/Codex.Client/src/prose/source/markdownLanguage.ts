import { markdown } from '@codemirror/lang-markdown';
import {
  HighlightStyle,
  LanguageDescription,
  LanguageSupport,
  syntaxHighlighting,
} from '@codemirror/language';
import { tags } from '@lezer/highlight';
import { latexLanguage, latexHighlightStyle } from './latex';

const LATEX = LanguageDescription.of({
  name: 'latex',
  support: new LanguageSupport(latexLanguage),
});

/** A fence tagged `{=latex}` is raw LaTeX; every other fence stays plain. */
function fenceLanguage(info: string): LanguageDescription | null {
  return info.trim() === '{=latex}' ? LATEX : null;
}

const markdownHighlightStyle = HighlightStyle.define([
  { tag: tags.heading, color: 'var(--color-text)', fontWeight: 'var(--weight-semibold)' },
  { tag: tags.strong, fontWeight: 'var(--weight-semibold)' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.strikethrough, textDecoration: 'line-through' },
  { tag: tags.link, color: 'var(--color-accent)' },
  { tag: tags.url, color: 'var(--color-muted)' },
  { tag: tags.monospace, fontFamily: 'var(--font-mono)' },
  { tag: [tags.quote, tags.contentSeparator], color: 'var(--color-muted)' },
  { tag: [tags.processingInstruction, tags.meta], color: 'var(--color-muted)' },
]);

/** Markdown with `{=latex}` fences highlighted as LaTeX, and the token colours for both. */
export const markdownHighlighting = [
  markdown({ codeLanguages: fenceLanguage }),
  syntaxHighlighting(markdownHighlightStyle),
  syntaxHighlighting(latexHighlightStyle),
];
