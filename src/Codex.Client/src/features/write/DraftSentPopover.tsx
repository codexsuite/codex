import { useQuery } from '@tanstack/react-query';
import { getDraftSent } from '@codex/core/api/ai';
import { writeTestIds } from './testIds';

interface DraftSentPopoverProps {
  generationId: string;
  target: string;
}

// What a generation sent (J22): the template, model and AI integration it used, the context entries by name, the token counts and the
// prompt text, all from the request stored when it ran, so a later template edit does not change it.
export function DraftSentPopover({ generationId, target }: DraftSentPopoverProps) {
  const sent = useQuery({
    queryKey: ['generation-sent', generationId],
    queryFn: ({ signal }) => getDraftSent(generationId, signal),
  });
  const d = sent.data;
  return (
    <div
      data-testid={writeTestIds.draftSentPopover(target)}
      role="dialog"
      aria-label="What was sent"
      className="write-draft-note flex flex-col gap-1"
    >
      {sent.isError && <span role="alert">Could not load what was sent.</span>}
      {d !== undefined && (
        <>
          <span>
            Template: {d.template ?? 'None'} · Model: {d.model}
            {d.integration !== null ? ` (${d.integration})` : ''}
          </span>
          <span>Context: {d.context.length > 0 ? d.context.join(', ') : 'none'}</span>
          <span>
            Tokens: {d.inputTokens ?? 0} in, {d.outputTokens ?? 0} out
          </span>
          <pre className="m-0 max-h-48 overflow-auto whitespace-pre-wrap font-mono text-xs">
            {d.instruction !== '' ? `${d.instruction}\n\n` : ''}
            {d.prompt}
          </pre>
        </>
      )}
    </div>
  );
}
