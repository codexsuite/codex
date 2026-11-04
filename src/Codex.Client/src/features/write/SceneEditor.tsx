import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { useOutline } from '../../api/outlineQueries';
import { sceneTarget } from '@codex/core/api/write';
import type { Editor } from '@tiptap/react';
import type { SaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { showToast } from '@codex/core/editing/toastStore';
import { useAutosave } from '../../editing/useAutosave';
import { MarkdownSurface } from '../../prose';
import { chapterLinkKey, chapterLinkPlugin } from './chapterLinkPlugin';
import type { ChapterLinkLabel } from '@codex/core/features/write/manuscriptView';
import { BeatCard } from './BeatCard';
import { DraftPanel } from './DraftPanel';
import { AdditionCard } from './AdditionCard';
import { FigureCard } from './FigureCard';
import { FigureDialog } from './FigureDialog';
import { ChapterPicker, type ChapterChoice } from './ChapterPicker';
import { citeSelectionKey, citeSelectionPlugin, citeTarget } from './citeSelection';
import { useNewSourceDetour } from './useNewSourceDetour';
import { EntryPicker } from './EntryPicker';
import { nameMatcher, useKbInfo } from './kbInfo';
import { kbMarksKey, kbMarksPlugin, type KbMarksSource } from './kbMarksPlugin';
import { useOpenMarkerCard } from './markerCardContext';
import { markersKey, markersPlugin, type MarkersSource } from './markersPlugin';
import { useMarkers } from './useMarkers';
import { useSceneRegistry } from './sceneRegistry';
import type { ServerCopy } from '@codex/core/features/write/serverCopy';
import { writeSlashItems } from './slashItems';
import { SourcePicker } from './SourcePicker';
import { useBlockOps } from './useBlockOps';
import { useCitations } from './useCitations';
import { useDraftActions } from './useDraftActions';
import { aiModelsQuery, defaultChoice, promptsQuery } from './useDraftSetup';
import { draftStore } from './draftStore';
import { useMentionEntries } from './useMentionEntries';
import { useWriteHighlight } from './useWriteHighlight';
import { useCanWrite } from '../../shell/bookAccess';
import { bookScreenLink, writeLink } from '@codex/core/shell/links';

interface SceneEditorProps {
  sceneId: string;
  /** What the server holds for this scene; block operations read it after settling. */
  copy: ServerCopy;
  initial: string;
  /** Text typed but never saved; it starts as an unsaved edit of `initial`. */
  draft?: string;
  version: number;
  save: (text: string, version: number) => Promise<{ version: number }>;
  store: SaveStatusStore;
  chapterLinks: ReadonlyMap<string, ChapterLinkLabel>;
}

// The live editor behind one scene: it owns the text being typed, saves it 3 s after the first edit and on blur
// (and when the scene leaves the screen), and draws chapter links from the live outline.
export function SceneEditor({
  sceneId,
  copy,
  initial,
  draft,
  version,
  save,
  store,
  chapterLinks,
}: SceneEditorProps) {
  const [text, setText] = useState(initial);
  const [editor, setEditor] = useState<Editor | null>(null);
  const labels = useRef(chapterLinks);
  const openArchived = useRef<(chapterId: string) => void>(() => {});

  // The autosave baseline is the first render's text (`initial`), so a kept draft is just a first edit.
  const [draftApplied, setDraftApplied] = useState(false);
  if (!draftApplied) {
    setDraftApplied(true);
    if (draft !== undefined) setText(draft);
  }

  const { bookId = '' } = useParams();
  // Below write (#80) the prose reads but takes no typing, so nothing is ever saved; a pending AI draft belongs to
  // whoever ran it and is not shown.
  const canWrite = useCanWrite();
  const registry = useSceneRegistry();
  const ops = useBlockOps();
  const entries = useMentionEntries(bookId);
  const infos = useKbInfo(bookId);
  const [highlighted] = useWriteHighlight(bookId);
  const navigate = useNavigate();
  // A link to an archived chapter opens Plan's Archived list at that chapter (rule R1).
  useEffect(() => {
    openArchived.current = (chapterId) =>
      void navigate(`${bookScreenLink(bookId, 'plan')}#archived=${encodeURIComponent(chapterId)}`);
  }, [navigate, bookId]);
  const queryClient = useQueryClient();
  const draftActions = useDraftActions();
  const outline = useOutline(bookId);
  const [picker, setPicker] = useState<{ editor: Editor; left: number; bottom: number } | null>(
    null,
  );
  const [additionPicker, setAdditionPicker] = useState<{
    editor: Editor;
    left: number;
    bottom: number;
  } | null>(null);
  // Insert figure…: the editor whose caret the figure goes at, while the dialog is open.
  const [figureDialog, setFigureDialog] = useState<Editor | null>(null);
  const citations = useCitations();
  const newSource = useNewSourceDetour();
  // While a citation is settled and sent the scene takes no typing: the range was read from this text, and the
  // server's answer replaces it.
  const [citing, setCiting] = useState(false);
  const [sourcePicker, setSourcePicker] = useState<{
    editor: Editor;
    range: { from: number; to: number };
    left: number;
    bottom: number;
  } | null>(null);

  const { flush } = useAutosave({ value: text, version, save, store });
  const flushNow = useRef(flush);
  useEffect(() => {
    flushNow.current = flush;
  });
  useEffect(() => {
    const leave = flushNow;
    return () => leave.current();
  }, []);

  const textNow = useRef(text);
  useEffect(() => {
    textNow.current = text;
  });
  const storeNow = useRef(store);
  useEffect(() => {
    storeNow.current = store;
  });
  useEffect(
    () =>
      registry.register(sceneId, {
        saved: () => ({ body: copy.body, version: copy.version }),
        // Saves what is typed and waits for it. Typing that arrives meanwhile (the menu has just removed the
        // "/query") takes another round; a round ends on a macrotask so the autosave has finished its own save.
        // A save already in flight (the blur's, sent before the menu removed its "/query") is waited for first:
        // until it lands the copy still holds the old body, which can match the text while the version is stale.
        settle: async () => {
          const landed = async () => {
            try {
              await copy.pending;
            } catch {
              return false;
            }
            await new Promise<void>((resolve) => setTimeout(resolve, 0));
            return true;
          };
          for (let round = 0; round < 5; round += 1) {
            if (copy.pending && !(await landed())) return false;
            if (copy.body === textNow.current) return storeNow.current.getState() !== 'conflict';
            flushNow.current();
            if (!(await landed())) return false;
          }
          return false;
        },
      }),
    [registry, sceneId, copy],
  );

  const chapterChoices = useMemo<ChapterChoice[]>(
    () =>
      (outline.data?.acts ?? [])
        .flatMap((a) => a.chapters)
        .filter((c) => !c.archived)
        .map((c) => ({
          id: c.id,
          title: c.title,
          label: c.number === null ? 'Unnumbered' : `Chapter ${c.number}`,
        })),
    [outline.data],
  );
  // "Continue writing": a draft from the end of the scene, with the author's pick for it or the Continue template.
  const continueWriting = useCallback(async () => {
    const target = sceneTarget(sceneId);
    const [prompts, models] = await Promise.all([
      queryClient.fetchQuery(promptsQuery),
      queryClient.fetchQuery(aiModelsQuery),
    ]);
    const fallback = defaultChoice(prompts, models);
    const picked = draftStore.choice(target);
    const choice =
      picked !== undefined &&
      models.integrations.some((i) => i.integrationId === picked.integrationId)
        ? picked
        : fallback === null
          ? null
          : {
              ...fallback,
              templateId:
                prompts.templates.find((t) => t.id === 'continue-writing')?.id ??
                fallback.templateId,
            };
    if (choice === null) {
      showToast({ message: 'Connect an AI provider in Settings to draft prose.' });
      return;
    }
    await draftActions.generate(
      { sceneId },
      {
        templateId: choice.templateId,
        model: choice.model,
        integrationId: choice.integrationId,
        targetWords: null,
        retryOfId: null,
      },
    );
  }, [sceneId, queryClient, draftActions]);
  const slashItems = useMemo(
    () =>
      writeSlashItems({
        sceneId,
        ops,
        continueWriting: () => void continueWriting(),
        openChapterPicker: (target) => {
          const caret = target.view.coordsAtPos(target.state.selection.from);
          setPicker({ editor: target, left: caret.left, bottom: caret.bottom });
        },
        openAdditionPicker: (target) => {
          const caret = target.view.coordsAtPos(target.state.selection.from);
          setAdditionPicker({ editor: target, left: caret.left, bottom: caret.bottom });
        },
        openFigureDialog: setFigureDialog,
        openSourcePicker: (target) => {
          const cited = citeTarget(target.state);
          if (!cited) return;
          // The space the kept selection's ` /` brought goes too, so the text is as it was.
          if (cited.space !== undefined) {
            target.commands.deleteRange({ from: cited.space, to: cited.space + 1 });
          }
          const caret = target.view.coordsAtPos(target.state.selection.from);
          setSourcePicker({
            editor: target,
            range: { from: cited.from, to: cited.to },
            left: caret.left,
            bottom: caret.bottom,
          });
        },
      }),
    [sceneId, ops, continueWriting],
  );
  const renderDirective = useCallback(
    ({ name, id }: { name: string; id: string | null }) =>
      name === 'beat' && id ? (
        <BeatCard beatId={id} sceneId={sceneId} entries={entries} />
      ) : name === 'kb-addition' && id ? (
        <AdditionCard additionId={id} entries={entries} infos={infos} />
      ) : name === 'figure' && id ? (
        <FigureCard
          figureId={id}
          bookId={bookId}
          onRemove={() => void ops.removeFigure(id, sceneId)}
        />
      ) : undefined,
    [sceneId, entries, infos, bookId, ops],
  );

  // What the Knowledge base marks read on every redraw; a change only needs a transaction to show.
  const marks = useRef<KbMarksSource>({
    infos: new Map(),
    match: () => [],
    highlighted: null,
    open: () => {},
  });
  useEffect(() => {
    marks.current = {
      infos,
      match: nameMatcher(infos),
      highlighted,
      open: (entryId) => void navigate(writeLink(bookId, { entry: entryId })),
    };
    if (editor && !editor.isDestroyed) {
      editor.view.dispatch(editor.state.tr.setMeta(kbMarksKey, 'redraw'));
    }
  }, [infos, highlighted, navigate, bookId, editor]);

  // The source markers, drawn the same way: read on every redraw, so new markers only need a transaction.
  const { byUse } = useMarkers(bookId);
  const openCard = useOpenMarkerCard();
  const markers = useRef<MarkersSource>({ byUse: new Map(), open: () => {} });
  useEffect(() => {
    markers.current = { byUse, open: openCard };
    if (editor && !editor.isDestroyed) {
      editor.view.dispatch(editor.state.tr.setMeta(markersKey, 'redraw'));
    }
  }, [byUse, openCard, editor]);

  const onEditor = useCallback((created: Editor) => {
    if (!citeSelectionKey.get(created.state)) {
      // First, so a `/` typed over a selection reaches it before any input rule.
      created.registerPlugin(citeSelectionPlugin(), (plugin, plugins) => [plugin, ...plugins]);
    }
    if (!markersKey.get(created.state)) {
      created.registerPlugin(markersPlugin(() => markers.current));
    }
    if (!chapterLinkKey.get(created.state)) {
      created.registerPlugin(
        chapterLinkPlugin(
          () => labels.current,
          (chapterId) => openArchived.current(chapterId),
        ),
      );
    }
    if (!kbMarksKey.get(created.state)) {
      created.registerPlugin(kbMarksPlugin(() => marks.current));
    }
    setEditor(created);
  }, []);

  useEffect(() => {
    labels.current = chapterLinks;
    if (editor && !editor.isDestroyed) {
      editor.view.dispatch(editor.state.tr.setMeta(chapterLinkKey, 'redraw'));
    }
  }, [chapterLinks, editor]);

  return (
    // The blur bubbles from the editor: leaving the scene saves it now.
    <div onBlur={flush}>
      <MarkdownSurface
        surface="scene-prose"
        variant="prose"
        value={text}
        onChange={setText}
        onEditor={onEditor}
        readOnly={citing || !canWrite}
        entries={entries}
        slashItems={slashItems}
        renderDirective={renderDirective}
      />
      {canWrite && <DraftPanel target={sceneTarget(sceneId)} sceneId={sceneId} />}
      {additionPicker && (
        <EntryPicker
          entries={entries}
          rect={additionPicker}
          placeholder="Codex addition for…"
          onClose={() => setAdditionPicker(null)}
          onPick={(entry) => {
            const target = additionPicker.editor;
            setAdditionPicker(null);
            void ops.insertAddition(sceneId, target, entry.id);
          }}
        />
      )}
      {figureDialog && (
        <FigureDialog
          bookId={bookId}
          mode="insert"
          onClose={() => setFigureDialog(null)}
          onSubmit={(fields) => {
            const target = figureDialog;
            setFigureDialog(null);
            void ops.insertFigure(sceneId, target, fields);
            return Promise.resolve(null);
          }}
        />
      )}
      {sourcePicker && (
        <SourcePicker
          bookId={bookId}
          rect={sourcePicker}
          onClose={() => setSourcePicker(null)}
          onPick={(source) => {
            const { editor: target, range } = sourcePicker;
            setSourcePicker(null);
            setCiting(true);
            void citations.cite(sceneId, target, range, source.id).finally(() => setCiting(false));
          }}
          onNewSource={() => {
            const { editor: target, range } = sourcePicker;
            setSourcePicker(null);
            setCiting(true);
            void newSource(sceneId, target, range).finally(() => setCiting(false));
          }}
        />
      )}
      {picker && (
        <ChapterPicker
          chapters={chapterChoices}
          rect={picker}
          onClose={() => setPicker(null)}
          onPick={(chapter) => {
            picker.editor
              .chain()
              .focus()
              .insertContent([
                {
                  type: 'text',
                  text: chapter.title,
                  marks: [{ type: 'chapterLink', attrs: { id: chapter.id } }],
                },
                { type: 'text', text: ' ' },
              ])
              .run();
            setPicker(null);
          }}
        />
      )}
    </div>
  );
}
