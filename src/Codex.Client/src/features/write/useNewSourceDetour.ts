import { useCallback } from 'react';
import type { Editor } from '@tiptap/react';
import { useNavigate, useParams } from 'react-router-dom';
import { useOutline } from '../../api/outlineQueries';
import { showToast } from '@codex/core/editing/toastStore';
import { serialize } from '../../prose';
import { referencesLink, writeLink } from '@codex/core/shell/links';
import { occurrencesUpTo, passageAt } from './citePassage';
import { selectionIn, selectionText } from './citeReturn';
import { useSceneRegistry } from './sceneRegistry';
import { cannotMark } from './useCitations';

/**
 * `/` → Source reference → New source…: saves the scene, then opens References' Add source with
 * `?return=<Write at this scene>&sel=<the words, as a range of the saved prose>`. Coming back is useReturnedCitation.
 */
export function useNewSourceDetour() {
  const { bookId = '' } = useParams();
  const registry = useSceneRegistry();
  const navigate = useNavigate();
  const outline = useOutline(bookId);

  return useCallback(
    async (sceneId: string, editor: Editor, range: { from: number; to: number }) => {
      const scene = registry.get(sceneId);
      if (!scene) {
        showToast({ message: 'Show the scene in Write to cite a new source in it.' });
        return;
      }
      const found = passageAt(editor.state.doc, range.from, range.to);
      if (!found) {
        showToast({ message: cannotMark });
        return;
      }
      if (!(await scene.settle())) {
        showToast({
          message:
            'The scene could not be saved, so Add source was not opened. Try again once it is saved.',
        });
        return;
      }
      const saved = scene.saved();
      // As when citing: a body stored in another form than the editor writes is trusted only for a single match.
      const sameForm = saved.body === serialize(editor.state.doc);
      if (!sameForm && occurrencesUpTo(saved.body, found.passage) !== 1) {
        showToast({
          message:
            'These words appear more than once in the scene as it is stored. Select a longer passage to cite.',
        });
        return;
      }
      const selection = selectionIn(
        saved.body,
        found.passage,
        sameForm ? found.occurrence : 1,
        saved.version,
      );
      if (!selection) {
        showToast({
          message: 'The words are not in the saved scene. Select them again and retry.',
        });
        return;
      }
      const number = outline.data?.acts
        .flatMap((act) => act.chapters)
        .flatMap((chapter) => chapter.scenes)
        .find((s) => s.id === sceneId)?.number;
      void navigate(
        referencesLink(bookId, {
          isNew: true,
          returnTo: writeLink(bookId, { scene: number }),
          sel: selectionText(selection),
        }),
      );
    },
    [bookId, registry, navigate, outline.data],
  );
}
