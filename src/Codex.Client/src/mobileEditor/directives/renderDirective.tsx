import type { ReactNode } from 'react';
import type { BridgeScene } from '@codex/core/editing/editorBridge';
import type { DirectiveInfo } from '../../prose/renderContext';
import { AdditionCard } from './AdditionCard';
import { BeatCard } from './BeatCard';

/**
 * The draw function for a scene's directive lines: its beats and Codex additions, from the scene the app sent. A line
 * the scene has no row for shows just its label, never the raw id; every other directive keeps the editor's default card.
 */
export function directiveRenderer(scene: BridgeScene) {
  return ({ name, id }: DirectiveInfo): ReactNode | undefined => {
    if (id === null) return undefined;
    if (name === 'beat') {
      const beat = scene.beats.find((b) => b.id === id);
      return beat ? <BeatCard beat={beat} /> : <span className="prose-card-label">Scene beat</span>;
    }
    if (name === 'kb-addition') {
      const addition = scene.additions.find((a) => a.id === id);
      return addition ? (
        <AdditionCard addition={addition} />
      ) : (
        <span className="prose-card-label">Codex addition</span>
      );
    }
    return undefined;
  };
}
