import { skipToken, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { manuscriptKey, type Draft, type Manuscript } from '@codex/core/api/write';
import { DraftSentPopover } from './DraftSentPopover';
import { beatProseWords } from './beatProse';
import { paragraphs } from '@codex/core/features/write/draftText';
import { DraftParagraph } from './DraftParagraph';
import { writeTestIds } from './testIds';
import { useDraftActions } from './useDraftActions';

interface PendingDraftPanelProps {
  draft: Draft;
  sceneId: string;
  beatId?: string;
  modelLabel: (modelId: string) => string;
}

// A finished draft waiting for a decision: Apply writes it into the scene, Retry asks again, Discard drops it.
export function PendingDraftPanel({ draft, sceneId, beatId, modelLabel }: PendingDraftPanelProps) {
  const { bookId = '' } = useParams();
  const actions = useDraftActions();
  // Reads the scene the screen already loaded; this panel never fetches it.
  const manuscript = useQuery<Manuscript>({ queryKey: manuscriptKey(bookId), queryFn: skipToken });
  const body = manuscript.data?.scenes.find((s) => s.sceneId === sceneId)?.body;
  const replaced = beatId !== undefined && body !== undefined ? beatProseWords(body, beatId) : 0;
  const target = beatId ?? `scene:${sceneId}`;
  const [showSent, setShowSent] = useState(false);
  return (
    <div
      data-testid={writeTestIds.draft(target)}
      data-state="pending"
      data-generation-id={draft.id}
      className="write-draft"
      contentEditable={false}
    >
      <div className="write-draft-head">
        <span className="prose-card-label">AI draft</span>
        <span data-testid={writeTestIds.draftMeta(target)} className="write-draft-meta">
          {draft.stopped
            ? `Stopped · ${draft.words} words · ${modelLabel(draft.model)}`
            : `${modelLabel(draft.model)} · ${draft.words} words`}
          {draft.attempt > 1 ? ` · attempt ${draft.attempt}` : ''}
        </span>
        <span className="flex-1" />
        <button
          type="button"
          data-testid={writeTestIds.draftSent(target)}
          aria-expanded={showSent}
          className="write-chip"
          onClick={() => setShowSent((open) => !open)}
        >
          Show what was sent
        </button>
        <button
          type="button"
          data-testid={writeTestIds.draftApply(target)}
          className="write-chip write-chip-primary"
          onClick={() => void actions.apply(draft)}
        >
          Apply
        </button>
        <button
          type="button"
          data-testid={writeTestIds.draftRetry(target)}
          className="write-chip"
          onClick={() =>
            void actions.generate(
              { sceneId, beatId },
              {
                templateId: draft.templateId,
                // A deleted integration leaves no id: an empty model means the default integration's own, not a foreign id.
                model: draft.integrationId ? draft.model : '',
                integrationId: draft.integrationId,
                targetWords: draft.targetWords,
                retryOfId: draft.id,
              },
            )
          }
        >
          Retry
        </button>
        <button
          type="button"
          data-testid={writeTestIds.draftDiscard(target)}
          className="write-chip"
          onClick={() => void actions.discard(draft)}
        >
          Discard
        </button>
      </div>
      {showSent && <DraftSentPopover generationId={draft.id} target={target} />}
      <div data-testid={writeTestIds.draftText(target)} className="write-draft-body">
        {paragraphs(draft.text).map((p, i) => (
          <DraftParagraph key={i} text={p} />
        ))}
      </div>
      {replaced > 0 && (
        <div data-testid={writeTestIds.draftNote(target)} className="write-draft-note">
          Apply replaces the {replaced.toLocaleString('en-US')} words below. The current text is
          kept in history.
        </div>
      )}
    </div>
  );
}
