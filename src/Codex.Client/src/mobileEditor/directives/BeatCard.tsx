import type { BridgeBeat } from '@codex/core/editing/editorBridge';
import { plainInstruction, wordsLabel } from '../format';
import { mobileEditorTestIds } from '../testIds';

// A `::beat` line, read-only on mobile in M2 (design/screens/mobile.html `.beat`): the label, the instruction and the
// target length. Hide, length choice, `+ Context` and Generate prose stay on the desktop. A hidden beat is the one-line
// row the desktop draws (no Show button here).
export function BeatCard({ beat }: { beat: BridgeBeat }) {
  if (beat.hidden) {
    return (
      <div className="me-beat-hidden" data-testid={mobileEditorTestIds.beatHidden(beat.id)}>
        <b className="me-beat-kind">Beat</b>
        <span className="me-beat-summary">
          <b>{beat.title || 'Untitled'}</b> {plainInstruction(beat.body)}
        </span>
        {beat.targetWordCount !== null && (
          <span className="me-beat-words">{beat.targetWordCount}w</span>
        )}
      </div>
    );
  }
  return (
    <div className="me-beat" data-testid={mobileEditorTestIds.beat(beat.id)}>
      <b>Scene beat{beat.title !== '' ? ` · ${beat.title}` : ''}</b>
      <span data-testid={mobileEditorTestIds.beatInstruction(beat.id)}>
        {plainInstruction(beat.body)}
      </span>
      {beat.targetWordCount !== null && (
        <span className="me-beat-length" data-testid={mobileEditorTestIds.beatLength(beat.id)}>
          {wordsLabel(beat.targetWordCount)}
        </span>
      )}
    </div>
  );
}
