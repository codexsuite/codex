import { EditorView } from '@codemirror/view';

// The source editor's look, from the design tokens only (light and dark follow `data-theme` through the variables).
export const sourceTheme = EditorView.theme({
  '&': {
    backgroundColor: 'var(--color-surface)',
    color: 'var(--color-text)',
    border: '1px solid var(--color-border)',
    borderRadius: 'var(--radius-sm)',
    fontSize: 'var(--text-sm)',
  },
  '&.cm-focused': { outline: '2px solid var(--color-accent)', outlineOffset: '-1px' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: 'var(--leading)',
    padding: 'var(--space-2) 0',
  },
  '.cm-content': { caretColor: 'var(--color-accent)', padding: '0 var(--space-3)' },
  '.cm-cursor': { borderLeftColor: 'var(--color-accent)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: 'var(--color-accent-soft)',
  },
  '.cm-placeholder': { color: 'var(--color-muted)' },
  '.cm-directive': { color: 'var(--color-info)' },
  '.cm-container': { color: 'var(--color-info)', fontWeight: 'var(--weight-medium)' },
  '.cm-inline-latex': { color: 'var(--category-research)' },
  '.cm-mention': { color: 'var(--category-person)', fontWeight: 'var(--weight-medium)' },
  '.cm-ref': { color: 'var(--category-law)' },
  '.cm-chapter': { color: 'var(--category-story)' },
  '.cm-id': { color: 'var(--color-muted)', opacity: '0.55', fontSize: '0.85em' },
});
