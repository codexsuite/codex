import { useEffect, useState, type ReactNode } from 'react';
import type { LiveDraft } from './draftStore';
import { countWords, paragraphs } from '@codex/core/features/write/draftText';
import { writeTestIds } from './testIds';
import { useDraftActions } from './useDraftActions';

interface LiveDraftPanelProps {
  live: LiveDraft;
  beatId?: string;
  modelLabel: (modelId: string) => string;
}

const RATE_LIMIT_WAIT_S = 30;

// A provider that answers 429 says so in its message; the panel then counts down before Retry is allowed.
const rateLimited = (error: string | null) => error !== null && /rate.?limit/i.test(error);

// Counts seconds down from `seconds` to 0 once mounted, and renders `children` with what is left.
function Countdown({
  seconds,
  children,
}: {
  seconds: number;
  children: (left: number) => ReactNode;
}) {
  const [left, setLeft] = useState(seconds);
  useEffect(() => {
    const timer = setInterval(() => setLeft((n) => Math.max(0, n - 1)), 1000);
    return () => clearInterval(timer);
  }, []);
  return <>{children(left)}</>;
}

// A generation in progress (text streams in with a caret and a Stop button), or one that failed (the message, with
// Retry and Dismiss).
export function LiveDraftPanel(props: LiveDraftPanelProps) {
  const { live } = props;
  return live.status === 'failed' && rateLimited(live.error) ? (
    <Countdown key={live.generationId ?? live.error} seconds={RATE_LIMIT_WAIT_S}>
      {(wait) => <LivePanel {...props} wait={wait} />}
    </Countdown>
  ) : (
    <LivePanel {...props} wait={null} />
  );
}

function LivePanel({
  live,
  beatId,
  modelLabel,
  wait,
}: LiveDraftPanelProps & { wait: number | null }) {
  const actions = useDraftActions();
  const { target } = live;
  const streaming = live.status !== 'failed';
  const retry = () =>
    void actions.generate(
      { sceneId: live.sceneId, beatId },
      { ...live.request, retryOfId: live.generationId ?? live.request.retryOfId },
    );
  return (
    <div
      data-testid={writeTestIds.draft(target)}
      data-state={live.status}
      className="write-draft"
      contentEditable={false}
    >
      <div className="write-draft-head">
        <span className="prose-card-label">{streaming ? 'Generating…' : 'Draft failed'}</span>
        <span data-testid={writeTestIds.draftMeta(target)} className="write-draft-meta">
          {modelLabel(live.request.model)} · {countWords(live.text)} words
          {live.attempt !== null && live.attempt > 1 ? ` · attempt ${live.attempt}` : ''}
        </span>
        <span className="flex-1" />
        {streaming ? (
          <button
            type="button"
            data-testid={writeTestIds.draftStop(target)}
            className="write-chip"
            onClick={() => actions.stop(target)}
          >
            Stop
          </button>
        ) : (
          <>
            <button
              type="button"
              data-testid={writeTestIds.draftRetry(target)}
              className="write-chip write-chip-primary"
              disabled={wait !== null && wait > 0}
              onClick={retry}
            >
              Retry
            </button>
            <button
              type="button"
              data-testid={writeTestIds.draftDiscard(target)}
              className="write-chip"
              onClick={() => actions.dismiss(target)}
            >
              Dismiss
            </button>
          </>
        )}
      </div>
      {live.status === 'failed' && (
        <p role="alert" data-testid={writeTestIds.draftError(target)} className="write-draft-error">
          {wait !== null
            ? `Rate limited. ${wait > 0 ? `Try again in ${wait} s` : 'Try again now'}`
            : live.error}
        </p>
      )}
      {live.text !== '' && (
        <div data-testid={writeTestIds.draftText(target)} className="write-draft-body">
          {paragraphs(live.text).map((p, i) => (
            <p key={i}>{p}</p>
          ))}
          {streaming && (
            <span
              data-testid={writeTestIds.draftCaret(target)}
              className="write-draft-caret"
              aria-hidden="true"
            />
          )}
        </div>
      )}
    </div>
  );
}
