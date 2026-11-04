import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type FocusEvent,
} from 'react';
import type { Editor } from '@tiptap/react';
import type { BridgeScene } from '@codex/core/editing/editorBridge';
import { ProseEditor } from '../prose/ProseEditor';
import type { MentionEntry } from '../prose/entries';
import { applyProse } from './applyProse';
import { SceneCard } from './card/SceneCard';
import { directiveRenderer } from './directives/renderDirective';
import type { BlockActions } from './runCommand';
import { mobileSlashItems } from './slashItems';
import { mobileEditorTestIds } from './testIds';

interface SceneSectionProps {
  scene: BridgeScene;
  /** How many times the app has replaced this scene (`setScene`); the editor takes the new text when it moves. */
  replaced: number;
  /** The scene being edited: its card shows the summary and chips. */
  active: boolean;
  readOnly: boolean;
  entries: readonly MentionEntry[];
  actionsFor: (sceneId: string) => BlockActions;
  placeholder: string;
  onOpenScene: (sceneId: string) => void;
  onChange: (sceneId: string, markdown: string) => void;
  onFocus: (sceneId: string, editor: Editor) => void;
  onBlur: (sceneId: string) => void;
  /** The text is about to be replaced by the app's: anything typed and not yet sent is no longer the truth. */
  onTextReplaced: (sceneId: string) => void;
  onEditor: (sceneId: string, editor: Editor | null) => void;
  /** Every change to the editor's document or selection. */
  onTransaction: (editor: Editor) => void;
  elementRef: (sceneId: string, element: HTMLElement | null) => void;
}

// One scene as the page draws it: its card, then its prose in the desktop's `ProseEditor`. The editor is given the
// scene's text once; later text from the app is applied through `applyProse` (keeping the cursor), because
// `ProseEditor` re-reads its `value` and would wipe what was typed.
export function SceneSection({
  scene,
  replaced,
  active,
  readOnly,
  entries,
  actionsFor,
  placeholder,
  onOpenScene,
  onChange,
  onFocus,
  onBlur,
  onTextReplaced,
  onEditor,
  onTransaction,
  elementRef,
}: SceneSectionProps) {
  const [initialProse] = useState(scene.prose);
  const [editor, setEditor] = useState<Editor | null>(null);
  const handled = useRef(replaced);
  const { id } = scene;

  const slashItems = useMemo(() => mobileSlashItems(actionsFor(id)), [actionsFor, id]);
  const renderDirective = useMemo(() => directiveRenderer(scene), [scene]);

  const took = useCallback(
    (created: Editor) => {
      setEditor(created);
      onEditor(id, created);
    },
    [id, onEditor],
  );
  useEffect(() => () => onEditor(id, null), [id, onEditor]);

  useEffect(() => {
    if (!editor) return;
    const report = () => onTransaction(editor);
    editor.on('transaction', report);
    return () => void editor.off('transaction', report);
  }, [editor, onTransaction]);

  // A layout effect, so the app's text is in the editor in the same commit that draws the new card: input that arrives
  // after the card shows is typed into the new text.
  useLayoutEffect(() => {
    if (!editor || handled.current === replaced) return;
    handled.current = replaced;
    onTextReplaced(id);
    // The fresh state reset the undo history: tell the app what Undo can do now.
    if (applyProse(editor, scene.prose)) onTransaction(editor);
  }, [editor, replaced, scene.prose, id, onTextReplaced, onTransaction]);

  const leaving = (event: FocusEvent<HTMLDivElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget)) onBlur(id);
  };

  return (
    <section
      ref={(element) => elementRef(id, element)}
      className="me-scene"
      data-testid={mobileEditorTestIds.scene(id)}
      data-scene-id={id}
    >
      <SceneCard scene={scene} open={active} onOpen={onOpenScene} />
      <div
        className="me-prose"
        data-testid={mobileEditorTestIds.sceneProse(id)}
        onFocus={() => editor && onFocus(id, editor)}
        onBlur={leaving}
      >
        <ProseEditor
          value={initialProse}
          onChange={(markdown) => onChange(id, markdown)}
          entries={entries}
          slashItems={slashItems}
          readOnly={readOnly}
          placeholder={placeholder}
          testId={mobileEditorTestIds.sceneEditor(id)}
          onEditor={took}
          renderDirective={renderDirective}
        />
      </div>
    </section>
  );
}
