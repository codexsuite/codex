import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { modelLabelFor, promptsQuery } from './useDraftSetup';
import { draftTarget } from '@codex/core/api/write';
import { useBookDrafts } from '../../api/writeQueries';
import { useLiveDraft } from './draftStore';
import { LiveDraftPanel } from './LiveDraftPanel';
import { PendingDraftPanel } from './PendingDraftPanel';

interface DraftPanelProps {
  /** The beat's id, or `scene:<id>` for the continuation at the end of a scene. */
  target: string;
  sceneId: string;
  beatId?: string;
}

// The tinted panel under a beat (or at the end of a scene). While a generation runs or has failed it shows that;
// otherwise the pending draft the server holds for the target, if any.
export function DraftPanel({ target, sceneId, beatId }: DraftPanelProps) {
  const { bookId = '' } = useParams();
  const live = useLiveDraft(target);
  const drafts = useBookDrafts(bookId);
  const prompts = useQuery(promptsQuery);
  const draft = drafts.data?.drafts.find((d) => draftTarget(d) === target);
  const modelLabel = (id: string) => modelLabelFor(prompts.data, id);

  if (live) {
    return <LiveDraftPanel live={live} beatId={beatId} modelLabel={modelLabel} />;
  }
  if (draft) {
    return (
      <PendingDraftPanel draft={draft} beatId={beatId} modelLabel={modelLabel} sceneId={sceneId} />
    );
  }
  return null;
}
