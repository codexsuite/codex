import { NodeViewContent, NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { proseTestIds } from '../testIds';

const LABELS: Record<string, string> = {
  readaloud: 'Read aloud',
  sidebar: 'Sidebar',
  aside: 'Aside',
};

export function ContainerView({ node }: NodeViewProps) {
  const name = node.attrs.name as string;
  const title = node.attrs.title as string | null;
  return (
    <NodeViewWrapper
      className="prose-container"
      data-container={name}
      data-testid={proseTestIds.containerCard(name)}
    >
      <div className="prose-card-label" contentEditable={false}>
        {LABELS[name] ?? name}
        {title ? `: ${title}` : ''}
      </div>
      <NodeViewContent />
    </NodeViewWrapper>
  );
}
