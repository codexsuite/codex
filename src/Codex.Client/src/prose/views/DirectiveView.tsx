import { useContext } from 'react';
import { NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { ProseRenderContext } from '../renderContext';
import { proseTestIds } from '../testIds';

const LABELS: Record<string, string> = {
  beat: 'Scene beat',
  'kb-addition': 'Codex addition',
  figure: 'Figure',
  spell: 'Spell',
  statblock: 'Stat block',
  pagebreak: 'Page break',
};

export function DirectiveView({ node }: NodeViewProps) {
  const { renderDirective } = useContext(ProseRenderContext);
  const name = node.attrs.name as string;
  const id = (node.attrs.ref as string | null) ?? null;
  return (
    <NodeViewWrapper
      className="prose-card"
      contentEditable={false}
      data-testid={proseTestIds.directiveCard(name)}
      data-directive={name}
      data-ref={id ?? undefined}
    >
      {renderDirective?.({ name, id }) ?? (
        <>
          <span className="prose-card-label">{LABELS[name] ?? name}</span>
          {id && <span className="prose-card-id">{id}</span>}
        </>
      )}
    </NodeViewWrapper>
  );
}
