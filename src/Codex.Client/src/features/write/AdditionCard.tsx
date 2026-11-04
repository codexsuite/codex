import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import {
  additionApi,
  setAddition,
  type KbAddition,
  type PatchKbAdditionRequest,
} from '@codex/core/api/write';
import { useBookAdditions } from '../../api/writeQueries';
import { showToast } from '@codex/core/editing/toastStore';
import { useCanWrite } from '../../shell/bookAccess';
import { MarkdownSurface, type MentionEntry } from '../../prose';
import { EntryPicker } from './EntryPicker';
import type { KbInfo } from './kbInfo';
import { useWriteHighlight } from './useWriteHighlight';
import { writeTestIds } from './testIds';

interface AdditionCardProps {
  additionId: string;
  entries: readonly MentionEntry[];
  infos: ReadonlyMap<string, KbInfo>;
}

// The card drawn for a `::kb-addition` line: the entry it is about (changeable) and the text that holds for that
// entry from this point in the story on. Changes apply at once and are put back if the server refuses them.
export function AdditionCard({ additionId, entries, infos }: AdditionCardProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const additions = useBookAdditions(bookId);
  const addition = additions.data?.additions.find((a) => a.id === additionId);
  const [highlighted] = useWriteHighlight(bookId);
  const [picker, setPicker] = useState<{ left: number; bottom: number } | null>(null);
  const chip = useRef<HTMLButtonElement>(null);
  // Below write (#80): the entry chip is a label and the text reads only.
  const canWrite = useCanWrite();
  // The text as typed, saved when focus leaves the field; `sent` is the text last sent (or received), so an addition
  // that comes back different from it (put back after a refusal, another tab) replaces what is shown.
  const [text, setText] = useState(addition?.body ?? '');
  const typed = useRef(text);
  const sent = useRef(text);
  const received = addition?.body;
  useEffect(() => {
    if (received !== undefined && received !== sent.current) {
      sent.current = received;
      typed.current = received;
      setText(received);
    }
  }, [received]);

  if (!addition) return <span className="prose-card-label">Codex addition</span>;

  const info = infos.get(addition.entryId);
  const change = async (request: PatchKbAdditionRequest, shown: Partial<KbAddition>) => {
    setAddition(queryClient, bookId, { ...addition, ...shown });
    try {
      setAddition(queryClient, bookId, await additionApi.patch(additionId, request));
    } catch (error) {
      setAddition(queryClient, bookId, addition);
      showToast({
        message: error instanceof Error ? error.message : 'The addition could not be saved.',
      });
    }
  };

  return (
    <div
      data-testid={writeTestIds.addition(additionId)}
      data-kb-entry={addition.entryId}
      className={`write-addition ${addition.entryId === highlighted ? 'kb-hl' : ''}`}
      style={{ ['--cat' as string]: info?.colour ?? 'var(--color-accent)' }}
    >
      <header className="flex items-center gap-2">
        <span className="prose-card-label write-addition-kind">Codex addition</span>
        <span className="flex-1" />
        <button
          ref={chip}
          type="button"
          data-testid={writeTestIds.additionEntry(additionId)}
          title={canWrite ? 'Change entry' : undefined}
          disabled={!canWrite}
          className="write-chip"
          onClick={() => {
            const box = chip.current?.getBoundingClientRect();
            setPicker({ left: box?.left ?? 0, bottom: box?.bottom ?? 0 });
          }}
        >
          <span className="write-addition-dot" />
          {info?.name ?? 'Entry'}
        </button>
      </header>
      <div
        className="write-addition-body"
        onBlur={(e) => {
          // The rich view writes a final newline after the last paragraph; the addition is stored without it.
          const body = typed.current.replace(/\n+$/, '');
          if (e.currentTarget.contains(e.relatedTarget) || body === sent.current) return;
          sent.current = body;
          void change({ body }, { body });
        }}
      >
        <MarkdownSurface
          surface="kb-addition"
          variant="prose"
          testId={writeTestIds.additionBody(additionId)}
          value={text}
          onChange={(markdown) => {
            typed.current = markdown;
            setText(markdown);
          }}
          readOnly={!canWrite}
          placeholder={
            canWrite ? 'What is true about this entry from this point in the story…' : undefined
          }
        />
      </div>
      <div className="write-addition-note">
        From here on, the AI reads this as part of <b>{info?.name ?? 'the entry'}</b>.
      </div>
      {picker && (
        <EntryPicker
          entries={entries}
          rect={picker}
          placeholder="Change entry…"
          onClose={() => setPicker(null)}
          onPick={(entry) => {
            setPicker(null);
            if (entry.id !== addition.entryId)
              void change({ entryId: entry.id }, { entryId: entry.id });
          }}
        />
      )}
    </div>
  );
}
