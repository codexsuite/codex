import { useEffect, useRef, useState } from 'react';
import type { OutlineScene } from '@codex/core/api/structure';
import { MarkdownSurface } from '../../prose';
import { planTestIds } from './testIds';
import { usePlanEdit } from './usePlanEdit';
import { useSceneField } from './useSceneField';
import { useCanWrite } from '../../shell/bookAccess';

const FOCUS_ATTEMPTS = 30;

// Focuses the editor's contenteditable once TipTap has mounted it.
function focusEditor(root: HTMLElement | null) {
  let attempts = 0;
  let frame = 0;
  const tryFocus = () => {
    const editable = root?.querySelector<HTMLElement>('[contenteditable="true"]');
    if (editable) {
      editable.focus();
    } else if (attempts++ < FOCUS_ATTEMPTS) {
      frame = requestAnimationFrame(tryFocus);
    }
  };
  tryFocus();
  return () => cancelAnimationFrame(frame);
}

const PLACEHOLDER = 'Summarise this scene… (@ to mention Codex)';

// The scene summary, edited in place with `@` mentions. An empty summary shows the placeholder until it is clicked (or the
// scene was just created), then the editor takes over; it goes back to the note if left empty.
export function SceneSummary({ scene }: { scene: OutlineScene }) {
  const { entries, focusSceneId, setFocusSceneId } = usePlanEdit();
  const { value, setValue, flush } = useSceneField(scene, 'summary');
  const root = useRef<HTMLDivElement>(null);
  const [editing, setEditing] = useState(false);
  const canWrite = useCanWrite();
  const wantsFocus = focusSceneId === scene.id;
  const showEditor = value.trim() !== '' || editing || wantsFocus;
  useEffect(() => {
    if (!wantsFocus) return undefined;
    return focusEditor(root.current);
  }, [wantsFocus]);

  // Below write (#80) a summary reads, and an empty one shows nothing to click.
  if (!canWrite) {
    return (
      <div data-testid={planTestIds.sceneSummary(scene.id)} className="text-base">
        {value.trim() !== '' && (
          <MarkdownSurface
            surface="scene-summary"
            variant="summary"
            value={value}
            readOnly
            entries={entries}
          />
        )}
      </div>
    );
  }

  return (
    <div
      ref={root}
      data-testid={planTestIds.sceneSummary(scene.id)}
      onBlur={(event) => {
        if (event.relatedTarget instanceof Node && root.current?.contains(event.relatedTarget))
          return;
        flush();
        setEditing(false);
        if (wantsFocus) setFocusSceneId(null);
      }}
      className="text-base"
    >
      {showEditor ? (
        <MarkdownSurface
          surface="scene-summary"
          variant="summary"
          value={value}
          onChange={setValue}
          entries={entries}
          placeholder={PLACEHOLDER}
        />
      ) : (
        <button
          type="button"
          data-testid={planTestIds.sceneSummaryEmpty(scene.id)}
          onClick={() => {
            setEditing(true);
            focusEditor(root.current);
          }}
          className="block w-full border-0 bg-transparent p-0 text-left text-base text-muted"
        >
          {PLACEHOLDER}
        </button>
      )}
    </div>
  );
}
