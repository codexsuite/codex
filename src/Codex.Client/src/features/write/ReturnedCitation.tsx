import { useEffect, useRef } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { showToast } from '@codex/core/editing/toastStore';
import { writeLink } from '@codex/core/shell/links';
import { useDeepLink } from '../../shell/useDeepLink';
import { parseSelection } from './citeReturn';
import { resolveTarget, type ActView } from '@codex/core/features/write/manuscriptView';
import { useSceneRegistry } from './sceneRegistry';
import { useCitations } from './useCitations';

// Back from References' Add source with `?sel=…#scene=…&cite=<source>&title=<title>`: once the scene's editor is up,
// the saved selection is cited for the new source and a toast offers Undo. The answer is used once: the link is
// replaced by the plain scene link straight away, so a reload does not cite again. Draws nothing.
export function ReturnedCitation({ acts }: { acts: readonly ActView[] }) {
  const { bookId = '' } = useParams();
  const deepLink = useDeepLink();
  const registry = useSceneRegistry();
  const citations = useCitations();
  const navigate = useNavigate();
  const handled = useRef<string | null>(null);

  const link = deepLink.ok ? deepLink.link : null;
  const sourceId = link?.cite;
  const sel = link?.sel;
  const scene = link?.scene;
  const title = link?.title;
  const target = link ? resolveTarget(acts, link) : null;
  const sceneId = target?.kind === 'scene' ? target.id : undefined;

  useEffect(() => {
    if (sourceId === undefined || sel === undefined || sceneId === undefined) return;
    const key = `${sceneId}|${sourceId}|${sel}`;
    if (handled.current === key) return;
    handled.current = key;
    void navigate(writeLink(bookId, { scene }), { replace: true });
    const selection = parseSelection(sel);
    if (!selection) {
      showToast({
        message: 'The words to cite were lost on the way back. Select them and cite again.',
      });
      return;
    }
    void (async () => {
      if (!(await registry.whenRegistered(sceneId))) {
        showToast({
          message: `Scene ${scene ?? ''} is not shown in Write, so ${title ?? 'the source'} was not cited. Show the whole book and cite it again.`,
        });
        return;
      }
      const useId = await citations.citeSaved(sceneId, selection, sourceId);
      if (useId === null) return;
      showToast({
        message: `Cited ${title ?? 'the source'}`,
        undo: async () => {
          await citations.remove({ useId, sceneId });
        },
      });
    })();
  }, [bookId, sourceId, sel, scene, title, sceneId, navigate, registry, citations]);

  return null;
}
