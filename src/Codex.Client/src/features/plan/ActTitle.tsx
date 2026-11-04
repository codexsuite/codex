import { useEffect, useRef } from 'react';
import type { OutlineAct } from '@codex/core/api/structure';
import { planTestIds } from './testIds';
import { useActTitle } from './useActTitle';
import { usePlanEdit } from './usePlanEdit';
import { useCanWrite } from '../../shell/bookAccess';

// `Act 1: <title>`: the muted marker, then the title edited in place ("Name this act" while it is empty). Enter
// saves; a new act, or ✎ Rename act, puts the caret here.
export function ActTitle({ act, index }: { act: OutlineAct; index: number }) {
  const { value, setValue, flush } = useActTitle(act);
  const { focusActId, setFocusActId } = usePlanEdit();
  const input = useRef<HTMLInputElement>(null);
  const canWrite = useCanWrite();
  const wantsFocus = focusActId === act.id;

  useEffect(() => {
    if (!wantsFocus) return;
    input.current?.focus();
    input.current?.select();
    setFocusActId(null);
  }, [wantsFocus, setFocusActId]);

  return (
    <h2 className="m-0 flex min-w-0 flex-1 gap-1.5 text-lg font-semibold text-text">
      <span className="whitespace-nowrap text-muted">
        Act {index + 1}
        {value.trim() === '' ? '' : ':'}
      </span>
      <input
        ref={input}
        data-testid={planTestIds.actTitle(act.id)}
        aria-label={`Title of act ${index + 1}`}
        placeholder={canWrite ? 'Name this act' : undefined}
        readOnly={!canWrite}
        value={value}
        onChange={(event) => setValue(event.target.value)}
        onBlur={flush}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
        }}
        className="field-sizing-content max-w-full min-w-[8ch] rounded-xs border-0 bg-transparent p-0 font-semibold text-text placeholder:font-normal placeholder:text-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      />
    </h2>
  );
}
