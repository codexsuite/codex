import { useRef, useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { checkKbAlias } from '@codex/core/api/kbEntries';
import { PlusIcon } from './icons';
import { miniButton } from './kbStyles';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface AliasListProps {
  entryId: string;
  aliases: string[];
  /** Saves the whole alias set; rejects with the server's message (a collision is a 409). `allowDuplicate` saves a name another entry has. */
  onChange: (aliases: string[], allowDuplicate?: boolean) => Promise<unknown>;
}

// The other names that count as this entry in the text. A name another entry already has is refused by the server
// and its message is shown under the field.
export function AliasList({ entryId, aliases, onChange }: AliasListProps) {
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState('');
  const [error, setError] = useState<string | null>(null);
  // The alias that another entry already has, held back until Add anyway.
  const [held, setHeld] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const busy = useRef(false);

  // 'conflict' is another entry already answering to the alias: the typed text is held until Add anyway.
  const save = async (
    next: string[],
    allowDuplicate = false,
  ): Promise<'ok' | 'conflict' | 'failed'> => {
    try {
      await onChange(next, allowDuplicate);
      setError(null);
      setHeld(null);
      return 'ok';
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'The aliases could not be saved.');
      return e instanceof ApiRequestError && e.status === 409 ? 'conflict' : 'failed';
    }
  };

  // Enter or leaving the field adds what was typed; Esc drops it. A refused alias stays in the field with the
  // server's message under it.
  const finish = async (keep: boolean) => {
    if (busy.current) return;
    busy.current = true;
    const text = draft.trim();
    if (keep && text !== '' && !(await add(text))) {
      busy.current = false;
      return;
    }
    if (!keep) {
      setError(null);
      setHeld(null);
    }
    setDraft('');
    setAdding(false);
  };

  // Saves the alias. Another entry's name or alias is refused by the server (409) and held with Add anyway. A short or
  // common alias is saved, with a warning of how many words it underlines.
  const add = async (text: string): Promise<boolean> => {
    setWarning(null);
    const result = await save([...aliases, text]);
    if (result === 'conflict') setHeld(text);
    if (result !== 'ok') return false;
    // The count is advice: when it cannot be made, the alias is saved without a warning.
    const check = await checkKbAlias(entryId, text).catch(() => null);
    if (check?.warn) {
      setWarning(
        `Matches ${check.matches} other ${check.matches === 1 ? 'word' : 'words'} in the book. Underlines every "${text}".`,
      );
    }
    return true;
  };

  const addAnyway = async () => {
    if (held === null) return;
    if ((await save([...aliases, held], true)) === 'ok') {
      setDraft('');
      setAdding(false);
    }
  };

  // Below write (#80) aliases list, with no add or remove.
  const canWrite = useCanWrite();
  return (
    <div data-testid={kbTestIds.aliases} className="shrink-0 px-4">
      <div className="mt-0.5 flex min-h-[26px] flex-wrap items-center gap-1 text-xs">
        <span className="mr-0.5 text-muted">Also matches</span>
        {aliases.map((alias) => (
          <span
            key={alias}
            data-testid={kbTestIds.alias(alias)}
            className="inline-flex h-[19px] items-center gap-[3px] rounded-[10px] bg-surface-2 pr-[3px] pl-[7px] text-xs whitespace-nowrap"
          >
            {alias}
            {canWrite && (
              <button
                type="button"
                data-testid={kbTestIds.aliasRemove(alias)}
                aria-label={`Remove ${alias}`}
                onClick={() => void save(aliases.filter((a) => a !== alias))}
                className="inline-grid h-3.5 w-3.5 place-items-center rounded-full border-0 bg-transparent p-0 text-xs leading-none text-muted hover:bg-border hover:text-text"
              >
                ×
              </button>
            )}
          </span>
        ))}
        {!canWrite ? null : adding ? (
          <input
            autoFocus
            type="text"
            data-testid={kbTestIds.aliasInput}
            aria-label="New alias"
            placeholder="Another name, then Enter"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value);
              setHeld(null);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                void finish(true);
              } else if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                void finish(false);
              }
            }}
            onBlur={() => {
              if (held === null) void finish(true);
            }}
            className="h-5 w-[140px] rounded-[10px] border border-accent bg-bg px-[7px] text-xs text-text outline-none"
          />
        ) : (
          <button
            type="button"
            data-testid={kbTestIds.addAlias}
            title="Other names that should count as this entry in the text"
            onClick={() => {
              busy.current = false;
              setAdding(true);
            }}
            className={miniButton}
          >
            <PlusIcon size={11} />
            Alias
          </button>
        )}
      </div>
      {error && (
        <p data-testid={kbTestIds.aliasError} role="alert" className="m-0 mt-1 text-xs text-danger">
          {error}
          {held !== null && (
            <button
              type="button"
              data-testid={kbTestIds.aliasAddAnyway}
              onClick={() => void addAnyway()}
              className="ml-2 h-5 rounded-xs border border-border bg-transparent px-1.5 text-xs text-text hover:bg-surface-2"
            >
              Add anyway
            </button>
          )}
        </p>
      )}
      {warning && (
        <p
          data-testid={kbTestIds.aliasWarning}
          role="status"
          className="m-0 mt-1 text-xs text-warn"
        >
          {warning}
        </p>
      )}
    </div>
  );
}
