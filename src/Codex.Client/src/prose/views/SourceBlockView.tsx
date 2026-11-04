import { NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { proseTestIds } from '../testIds';

/** Opaque (unrecognised) blocks: shown as read-only source so nothing is hidden or lost. */
export function SourceBlockView({ node }: NodeViewProps) {
  return (
    <NodeViewWrapper
      className="prose-card"
      contentEditable={false}
      data-testid={proseTestIds.opaqueCard}
    >
      <span className="prose-card-label">Unrecognised block</span>
      <pre className="prose-card-source">{node.attrs.raw as string}</pre>
    </NodeViewWrapper>
  );
}
