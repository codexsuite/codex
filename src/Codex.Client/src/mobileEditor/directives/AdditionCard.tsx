import type { BridgeAddition } from '@codex/core/editing/editorBridge';
import { plainInstruction } from '../format';
import { mobileEditorTestIds } from '../testIds';

// A `::kb-addition` line: the entry it belongs to and the note.
export function AdditionCard({ addition }: { addition: BridgeAddition }) {
  return (
    <div className="me-beat" data-testid={mobileEditorTestIds.addition(addition.id)}>
      <b>Codex addition · {addition.entryName}</b>
      <span>{plainInstruction(addition.body)}</span>
    </div>
  );
}
