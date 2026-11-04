import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { aiSettingsLink } from '../auth/links';
import { setBeatContext } from '@codex/core/api/kb';
import { blockApi, setBeat, type Beat, type PatchBeatRequest } from '@codex/core/api/write';
import { useBookBeats, useBookDrafts } from '../../api/writeQueries';
import { showToast } from '@codex/core/editing/toastStore';
import { useCanWrite } from '../../shell/bookAccess';
import { MarkdownSurface, type MentionEntry } from '../../prose';
import { beatDrag } from './beatDrag';
import { ActionsMenu } from './ActionsMenu';
import { BeatContextPicker } from './BeatContextPicker';
import { BeatLength } from './BeatLength';
import { DraftChoicePicker } from './DraftChoicePicker';
import { DraftPanel } from './DraftPanel';
import { useLiveDraft } from './draftStore';
import {
  BeatIcon,
  ChevronDownIcon,
  ClearIcon,
  EyeIcon,
  EyeOffIcon,
  GripIcon,
  PlayIcon,
  PlusIcon,
} from './icons';
import { useKbInfo } from './kbInfo';
import { writeTestIds } from './testIds';
import { useBlockOps } from './useBlockOps';
import { useDraftActions } from './useDraftActions';
import { modelLabelFor, useDraftSetup } from './useDraftSetup';

// The rich view writes a final newline after the last paragraph; the instruction is stored without it.
const trimEnd = (markdown: string) => markdown.replace(/\n+$/, '');

interface BeatCardProps {
  beatId: string;
  sceneId: string;
  entries: readonly MentionEntry[];
}

// The card drawn for a `::beat` line (design/screens/write.html `.beat`): a header with the grip, `SCENE BEAT`, an
// optional title, Hide and ⋮; the instruction; target length and the Knowledge base entries sent as context; then
// Generate with its template · model, and Clear beat. The AI draft sits under the card. Changes are applied at once and put back if the server refuses them. The grip drags the beat and its
// prose to another place (or another scene); Alt+arrows on the grip step it.
export function BeatCard({ beatId, sceneId, entries }: BeatCardProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const ops = useBlockOps();
  const beats = useBookBeats(bookId);
  const beat = beats.data?.beats.find((b) => b.id === beatId);
  const [picking, setPicking] = useState(false);
  const [choosing, setChoosing] = useState(false);
  // The instruction as typed. It is saved when focus leaves the field, so `sent` is the text last sent (or received):
  // a beat that comes back from the server different from that (undo, another tab) replaces what is shown.
  const [text, setText] = useState(beat?.body ?? '');
  const typed = useRef(text);
  const sent = useRef(text);
  const actions = useDraftActions();
  const setup = useDraftSetup(beatId);
  const live = useLiveDraft(beatId);
  const drafts = useBookDrafts(bookId);
  const pending = drafts.data?.drafts.find((d) => d.beatId === beatId);
  const grip = useRef<HTMLButtonElement>(null);
  const colours = useKbInfo(bookId);
  // Below write (#80) the beat reads (title and instruction) with no grip, controls or drafts.
  const canWrite = useCanWrite();

  const loaded = beat !== undefined;
  const received = beat?.body;
  useEffect(() => {
    if (received !== undefined && received !== sent.current) {
      sent.current = received;
      typed.current = received;
      setText(received);
    }
  }, [received]);

  // A native listener, so the drag starts before ProseMirror (which wraps this card) can claim it.
  useEffect(() => {
    const element = grip.current;
    if (!element) return;
    const start = (event: DragEvent) => {
      event.stopPropagation();
      event.dataTransfer?.setData('text/plain', beatId);
      beatDrag.start({ beatId, sceneId });
    };
    const end = () => beatDrag.end();
    element.addEventListener('dragstart', start);
    element.addEventListener('dragend', end);
    return () => {
      element.removeEventListener('dragstart', start);
      element.removeEventListener('dragend', end);
    };
  }, [beatId, sceneId, loaded]);

  if (!beat) return <span className="prose-card-label">Scene beat</span>;

  const change = async (shown: Partial<Beat>, call: () => Promise<Beat | void>) => {
    setBeat(queryClient, bookId, { ...beat, ...shown });
    try {
      const saved = await call();
      if (saved) setBeat(queryClient, bookId, saved);
    } catch (error) {
      setBeat(queryClient, bookId, beat);
      showToast({
        message: error instanceof Error ? error.message : 'The beat could not be saved.',
      });
    }
  };
  const patch = (request: PatchBeatRequest, shown: Partial<Beat>) =>
    change(shown, () => blockApi.patchBeat(beatId, request));
  const setContext = (entryIds: string[]) =>
    change({ contextEntryIds: entryIds }, () => setBeatContext(beatId, entryIds));
  const saveBody = async () => {
    const body = trimEnd(typed.current);
    if (body === sent.current && body === beat.body) return;
    sent.current = body;
    await patch({ body }, { body });
  };
  const byId = new Map(entries.map((e) => [e.id, e]));
  const generate = async () => {
    const choice = setup.choice;
    if (!choice || live) return;
    // The model reads the saved beat, so a changed instruction is saved first.
    await saveBody();
    await actions.generate(
      { sceneId, beatId },
      {
        templateId: choice.templateId,
        model: choice.model,
        integrationId: choice.integrationId,
        targetWords: beat.targetWordCount,
        retryOfId: pending?.id ?? null,
      },
    );
  };
  const templateName =
    setup.prompts?.templates.find((t) => t.id === setup.choice?.templateId)?.name ?? 'No template';
  const modelLabel = modelLabelFor(setup.prompts, setup.choice?.model ?? '');
  const noProvider = setup.models !== undefined && setup.models.integrations.length === 0;

  const gripButton = canWrite && (
    <button
      ref={grip}
      type="button"
      draggable
      data-testid={writeTestIds.beatGrip(beatId)}
      aria-label="Move beat (drag, or Alt+arrow keys)"
      className="write-beat-grip"
      onKeyDown={(e) => {
        if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
        e.preventDefault();
        void ops.stepBeat(beatId, sceneId, e.key === 'ArrowUp' ? 'up' : 'down');
      }}
    >
      <GripIcon />
    </button>
  );

  if (beat.isHidden) {
    return (
      <>
        <div className="write-beat write-beat-hidden">
          {gripButton}
          <span className="write-beat-kind">
            <BeatIcon />
            Beat
          </span>
          <span className="write-beat-summary">
            <b>{beat.title || 'Untitled'}</b> {beat.body}
          </span>
          {beat.targetWordCount !== null && (
            <span className="write-words">{beat.targetWordCount}w</span>
          )}
          {canWrite && (
            <button
              type="button"
              data-testid={writeTestIds.beatShow(beatId)}
              className="write-mini"
              onClick={() => void patch({ isHidden: false }, { isHidden: false })}
            >
              <EyeIcon />
              Show
            </button>
          )}
        </div>
        {canWrite && <DraftPanel target={beatId} sceneId={sceneId} beatId={beatId} />}
      </>
    );
  }

  const menuItem = (testId: string, content: ReactNode, act: () => void, danger = false) => (
    <button
      type="button"
      role="menuitem"
      data-testid={testId}
      data-danger={danger || undefined}
      className="write-menu-item"
      onClick={act}
    >
      {content}
    </button>
  );

  return (
    <>
      <div className="write-beat relative">
        <header className="write-beat-h">
          {gripButton}
          <span className="write-beat-kind">
            <BeatIcon />
            Scene beat
          </span>
          <input
            data-testid={writeTestIds.beatTitle(beatId)}
            aria-label="Beat title"
            placeholder="Title (optional)"
            spellCheck={false}
            className="write-beat-title"
            readOnly={!canWrite}
            defaultValue={beat.title}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.currentTarget.blur();
            }}
            onBlur={(e) => {
              const title = e.target.value.trim();
              if (title !== beat.title) void patch({ title }, { title });
            }}
          />
          <span className="flex-1" />
          {canWrite && (
            <>
              <button
                type="button"
                data-testid={writeTestIds.beatHide(beatId)}
                title="Hide the beat; its prose stays"
                className="write-mini"
                onClick={() => void patch({ isHidden: true }, { isHidden: true })}
              >
                <EyeOffIcon />
                Hide
              </button>
              <ActionsMenu
                label="Beat actions"
                buttonTestId={writeTestIds.beatMore(beatId)}
                menuTestId={writeTestIds.beatMenu(beatId)}
              >
                {(close) => (
                  <>
                    {menuItem(
                      writeTestIds.beatMoveUp(beatId),
                      <>
                        Move up<span className="write-menu-key">Alt+↑</span>
                      </>,
                      () => {
                        close();
                        void ops.stepBeat(beatId, sceneId, 'up');
                      },
                    )}
                    {menuItem(
                      writeTestIds.beatMoveDown(beatId),
                      <>
                        Move down<span className="write-menu-key">Alt+↓</span>
                      </>,
                      () => {
                        close();
                        void ops.stepBeat(beatId, sceneId, 'down');
                      },
                    )}
                    <div className="write-menu-sep" />
                    {menuItem(
                      writeTestIds.beatDeleteKeepProse(beatId),
                      'Delete beat, keep prose',
                      () => {
                        close();
                        void ops.deleteBeat(beatId, sceneId, true);
                      },
                      true,
                    )}
                    {menuItem(
                      writeTestIds.beatDelete(beatId),
                      'Delete beat and prose',
                      () => {
                        close();
                        void ops.deleteBeat(beatId, sceneId, false);
                      },
                      true,
                    )}
                  </>
                )}
              </ActionsMenu>
            </>
          )}
        </header>
        <div
          className="write-beat-body"
          onKeyDownCapture={(e) => {
            if (canWrite && e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
              e.preventDefault();
              e.stopPropagation();
              void generate();
            }
          }}
          onBlur={(e) => {
            if (canWrite && !e.currentTarget.contains(e.relatedTarget)) void saveBody();
          }}
        >
          <MarkdownSurface
            surface="beat-instruction"
            variant="prose"
            testId={writeTestIds.beatBody(beatId)}
            value={text}
            onChange={(markdown) => {
              typed.current = markdown;
              setText(markdown);
            }}
            readOnly={!canWrite}
            placeholder={
              canWrite ? 'Describe what happens in this beat… (Ctrl+Enter to generate)' : undefined
            }
          />
        </div>
        {canWrite && (
          <>
            <div className="write-beat-row">
              <BeatLength
                beatId={beatId}
                words={beat.targetWordCount}
                onChange={(words) =>
                  void patch({ targetWordCount: words }, { targetWordCount: words })
                }
              />
              <span className="write-vr" />
              <button
                type="button"
                data-testid={writeTestIds.beatContextAdd(beatId)}
                title="Extra Knowledge base entries sent to the model for this beat"
                className="write-mini"
                onClick={() => setPicking(true)}
              >
                <PlusIcon />
                Context
              </button>
              {beat.contextEntryIds.map((id) => (
                <button
                  key={id}
                  type="button"
                  data-testid={writeTestIds.beatContextChip(beatId, id)}
                  title="Remove from this beat's context"
                  className="write-cx"
                  style={{ '--cat': colours.get(id)?.colour } as CSSProperties}
                  onClick={() => void setContext(beat.contextEntryIds.filter((e) => e !== id))}
                >
                  <i />
                  {byId.get(id)?.name ?? 'Entry'}
                  <span className="write-cx-x">×</span>
                </button>
              ))}
              {picking && (
                <BeatContextPicker
                  entries={entries}
                  chosen={beat.contextEntryIds}
                  onClose={() => setPicking(false)}
                  onPick={(entry) => {
                    setPicking(false);
                    void setContext([...beat.contextEntryIds, entry.id]);
                  }}
                />
              )}
            </div>
            <footer className="write-beat-f">
              {noProvider ? (
                <Link
                  to={aiSettingsLink()}
                  data-testid={writeTestIds.draftConnect}
                  className="write-mini"
                >
                  Connect an AI provider in Settings
                </Link>
              ) : (
                <span className="write-split">
                  <button
                    type="button"
                    data-testid={writeTestIds.beatGenerate(beatId)}
                    disabled={
                      (live !== undefined && live.status !== 'failed') || beat.body.trim() === ''
                    }
                    title={
                      beat.body.trim() === ''
                        ? 'Write the beat first'
                        : 'Generate prose (Ctrl+Enter)'
                    }
                    className="write-split-primary"
                    onClick={() => void generate()}
                  >
                    <PlayIcon />
                    {pending ? 'Regenerate' : 'Generate prose'}
                  </button>
                  <button
                    type="button"
                    data-testid={writeTestIds.beatChoice(beatId)}
                    aria-haspopup="dialog"
                    aria-expanded={choosing}
                    title="Prompt template and model"
                    onClick={() => setChoosing((open) => !open)}
                  >
                    <span className="write-split-choice">
                      {templateName} · {modelLabel}
                    </span>
                    <span className="write-split-chevron">
                      <ChevronDownIcon />
                    </span>
                  </button>
                </span>
              )}
              <span className="flex-1" />
              <button
                type="button"
                data-testid={writeTestIds.beatClear(beatId)}
                title="Clear the instruction"
                className="write-mini"
                onClick={() => {
                  typed.current = '';
                  sent.current = '';
                  setText('');
                  if (beat.body !== '') void patch({ body: '' }, { body: '' });
                }}
              >
                <ClearIcon />
                Clear beat
              </button>
              {choosing && setup.prompts && setup.models && setup.choice && (
                <DraftChoicePicker
                  prompts={setup.prompts}
                  models={setup.models}
                  choice={setup.choice}
                  onChoose={setup.choose}
                  onClose={() => setChoosing(false)}
                />
              )}
            </footer>
          </>
        )}
      </div>
      {canWrite && <DraftPanel target={beatId} sceneId={sceneId} beatId={beatId} />}
    </>
  );
}
