import { useState } from 'react';
import type { Permission } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

// The draft: the letter and its address can be edited; edits are saved when the field loses focus and sent along with Mark sent.
// Mark sent keeps focus on the field on mouse down: otherwise the blur save disables the button before the click lands.
export function PermissionDraft({
  permission,
  busy,
  onSave,
  onSend,
  onCopy,
  onWithdraw,
}: {
  permission: Permission;
  busy: boolean;
  onSave: (body: string | null, to: string | null) => Promise<boolean>;
  onSend: (body: string | null, to: string | null) => void;
  onCopy: (letter: string) => void;
  onWithdraw: () => void;
}) {
  const [letter, setLetter] = useState(permission.letter);
  const [to, setTo] = useState(permission.to);
  const changed = (): [string | null, string | null] => [
    letter === permission.letter ? null : letter,
    to === permission.to ? null : to,
  ];
  const save = () => {
    const [body, address] = changed();
    if (body !== null || address !== null) void onSave(body, address);
  };
  return (
    <>
      <label className="flex flex-col gap-1 text-sm text-muted">
        To
        <input
          type="text"
          value={to}
          placeholder="Rights holder's email"
          data-testid={refsTestIds.permTo}
          onChange={(event) => setTo(event.target.value)}
          onBlur={save}
          className="rounded-sm border border-border bg-surface p-1 text-sm text-text"
        />
      </label>
      <textarea
        aria-label="Request letter"
        rows={10}
        value={letter}
        data-testid={refsTestIds.permLetter}
        onChange={(event) => setLetter(event.target.value)}
        onBlur={save}
        className="rounded-sm border border-border bg-surface p-2 font-mono text-sm text-text"
      />
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy}
          data-testid={refsTestIds.permCopy}
          onClick={() => onCopy(letter)}
          className="rounded-sm border border-border px-3 py-1 text-sm text-text"
        >
          Copy letter
        </button>
        <button
          type="button"
          disabled={busy || letter.trim() === ''}
          data-testid={refsTestIds.permSend}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => onSend(...changed())}
          className="rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
        >
          Mark sent
        </button>
        <button
          type="button"
          disabled={busy}
          data-testid={refsTestIds.permWithdraw}
          onClick={onWithdraw}
          className="rounded-sm border border-border px-3 py-1 text-sm text-muted"
        >
          Withdraw
        </button>
      </div>
    </>
  );
}
