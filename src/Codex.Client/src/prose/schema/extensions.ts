import { StarterKit } from '@tiptap/starter-kit';
import type { AnyExtension } from '@tiptap/react';
import {
  ChapterLinkMark,
  Container,
  Directive,
  Emphasis,
  InlineCode,
  Mention,
  OpaqueBlock,
  ProseDocument,
  ProseTable,
  ProseTableCell,
  ProseTableHeader,
  ProseTableRow,
  RawLatex,
  RawLatexInline,
  RefMark,
  RefMarker,
  Strikethrough,
  Strong,
  SummaryDocument,
} from './nodes';

/** The extension set behind the `prose` schema: everything a scene body can hold. */
export function proseExtensions(): AnyExtension[] {
  return [
    ProseDocument,
    StarterKit.configure({
      document: false,
      bold: false,
      italic: false,
      code: false,
      codeBlock: false,
      strike: false,
      underline: false,
      // Links open from the document's own markup; nothing here follows or auto-creates one.
      link: { openOnClick: false, autolink: false, linkOnPaste: false },
      trailingNode: false,
    }),
    Strong,
    Emphasis,
    Strikethrough,
    InlineCode,
    RefMark,
    ChapterLinkMark,
    Mention,
    RefMarker,
    RawLatexInline,
    Directive,
    Container,
    ProseTable,
    ProseTableRow,
    ProseTableHeader,
    ProseTableCell,
    RawLatex,
    OpaqueBlock,
  ];
}

/** The extension set behind the `summary` schema: one paragraph of text, bold, italic, code and mentions. */
export function summaryExtensions(): AnyExtension[] {
  return [
    SummaryDocument,
    StarterKit.configure({
      document: false,
      blockquote: false,
      bold: false,
      italic: false,
      code: false,
      codeBlock: false,
      bulletList: false,
      orderedList: false,
      listItem: false,
      listKeymap: false,
      hardBreak: false,
      heading: false,
      horizontalRule: false,
      strike: false,
      underline: false,
      link: false,
      trailingNode: false,
    }),
    Strong,
    Emphasis,
    InlineCode,
    Mention,
  ];
}
