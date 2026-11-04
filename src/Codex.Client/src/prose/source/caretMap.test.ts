import { describe, expect, it } from 'vitest';
import { parse } from '../markdown/parse';
import { markdownOffsetAt, positionAtMarkdownOffset, visibleOffsets } from './caretMap';

const KENT = '0199c0de-0000-7000-8000-000b0000000b';
const BEAT = '0199c0de-0000-7000-8000-000700000003';

/** The caret sits where `|` is in `marked`; returns Markdown with the same caret shown as `|`. */
function richToSource(markdown: string, textBefore: string): string {
  const doc = parse(markdown, 'prose');
  let pos = -1;
  doc.descendants((node, start) => {
    if (pos < 0 && node.isText) {
      const at = (node.text ?? '').indexOf(textBefore);
      if (at >= 0) pos = start + at + textBefore.length;
    }
    return pos < 0;
  });
  const offset = markdownOffsetAt(doc, pos, markdown);
  return `${markdown.slice(0, offset)}|${markdown.slice(offset)}`;
}

describe('caretMap', () => {
  it('MarkdownOffsetAt_WhenInsideEmphasis_ShouldSkipTheMarks', () => {
    expect(richToSource('One **bold** word.', 'bo')).toBe('One **bo|ld** word.');
  });

  it('MarkdownOffsetAt_WhenAfterDirectiveAndMention_ShouldIgnoreTheirIds', () => {
    const markdown = `First.\n\n::beat{id="${BEAT}"}\n\nSee [Kent Beck](kb:${KENT}) now.`;
    expect(richToSource(markdown, ' now')).toBe(
      `First.\n\n::beat{id="${BEAT}"}\n\nSee [Kent Beck](kb:${KENT}) now|.`,
    );
  });

  it('MarkdownOffsetAt_WhenInsideAHeadingOrList_ShouldSkipTheMarkers', () => {
    expect(richToSource('## Title here\n\n1. First\n2. Second', 'Sec')).toBe(
      '## Title here\n\n1. First\n2. Sec|ond',
    );
  });

  it('PositionAtMarkdownOffset_WhenRoundTripped_ShouldReturnToTheSameText', () => {
    const markdown = `Alpha **beta** gamma.\n\n:::aside\nHidden.\n:::\n\nDelta [Kent](kb:${KENT}) epsilon.`;
    const doc = parse(markdown, 'prose');
    for (const word of ['Alpha', 'beta', 'gamma', 'Delta', 'epsilon']) {
      const offset = markdown.indexOf(word) + 2;
      const pos = positionAtMarkdownOffset(doc, markdown, offset);
      expect(markdownOffsetAt(doc, pos, markdown)).toBe(offset);
    }
  });

  it('MarkdownOffsetAt_WhenAfterClosingPunctuation_ShouldKeepItBeforeTheCaret', () => {
    expect(richToSource('Hello world.\n\nNext one.', 'world.')).toBe('Hello world.|\n\nNext one.');
  });

  it('PositionAtMarkdownOffset_WhenAfterClosingPunctuation_ShouldLandAfterIt', () => {
    const markdown = 'Hello world.\n\nNext one.';
    const doc = parse(markdown, 'prose');
    const pos = positionAtMarkdownOffset(doc, markdown, 'Hello world.'.length);
    expect(doc.textBetween(1, pos)).toBe('Hello world.');
  });

  it('VisibleOffsets_WhenFencesCommentsAndLinkTargets_ShouldSkipThem', () => {
    const markdown =
      'a\n\n```{=latex}\n\\newpage\n```\n\n<!-- note -->\n\n[b](https://x.example/c) `\\d`{=latex} e';
    const letters = visibleOffsets(markdown).map((o) => markdown[o]);
    expect(letters.join('')).toBe('abe');
  });

  it('PositionAtMarkdownOffset_WhenAtTheStart_ShouldBeTheFirstTextPosition', () => {
    const doc = parse('Hello there.', 'prose');
    expect(positionAtMarkdownOffset(doc, 'Hello there.', 0)).toBe(1);
  });

  // The rich view draws an inline LaTeX chip's source as text, but as one atom: it counts for nothing here, like its
  // hidden `{=latex}` span on the Markdown side, so the caret does not drift around it.
  it('MarkdownOffsetAt_WhenAfterAnInlineLatexChip_ShouldNotCountTheChipText', () => {
    expect(richToSource('Go `\\textsc{abc}`{=latex} on now.', ' on')).toBe(
      'Go `\\textsc{abc}`{=latex} on| now.',
    );
  });

  it('MarkdownOffsetAt_WhenInATableCell_ShouldSkipThePipesAndDelimiterRow', () => {
    expect(richToSource('| Name | Role |\n|:-----|-----:|\n| Kent | Dev  |', 'Ke')).toBe(
      '| Name | Role |\n|:-----|-----:|\n| Ke|nt | Dev  |',
    );
  });
});
