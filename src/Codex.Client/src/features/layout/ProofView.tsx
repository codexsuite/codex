import { useEffect, useState } from 'react';
import { ErrorState } from '../../shell/ErrorState';
import { ProofBanner } from './ProofBanner';
import { ProofLog } from './ProofLog';
import { ProofPages } from './ProofPages';
import { layoutTestIds } from './testIds';
import { useProof } from './useProof';

interface ProofViewProps {
  bookId: string;
  chapterId: string;
  zoom: number;
  onInfo: (info: ProofInfo) => void;
}

export interface ProofInfo {
  pages: number | null;
  cached: boolean;
  waiting: boolean;
}

const WAITING = new Set(['queued', 'running']);

// Proof mode of the Layout view: one chapter compiled with the real template by the worker, shown with pdf.js. While it
// compiles the steps show; a failed compile shows its log; a finished one shows the pages under the banner.
// Mount it with a key per chapter, so a new chapter starts a new session.
export function ProofView({ bookId, chapterId, zoom, onInfo }: ProofViewProps) {
  const session = useProof(bookId, chapterId);
  const [logToggled, setLogToggled] = useState<boolean | null>(null);
  const { proof } = session;

  const waiting = proof === undefined || WAITING.has(proof.status);
  const pages = proof?.pageCount ?? null;
  const { cached } = session;
  useEffect(() => onInfo({ pages, cached, waiting }), [onInfo, pages, cached, waiting]);

  if (session.requestError !== null) {
    return (
      <ErrorState
        testId={layoutTestIds.proofError}
        title="The proof could not be made"
        detail={session.requestError}
      />
    );
  }

  const failed = proof?.status === 'failed' || proof?.status === 'cancelled';
  const logOpen = logToggled ?? failed;
  return (
    <div data-testid={layoutTestIds.proofView} className="flex min-h-0 flex-1 flex-col">
      {proof && (
        <ProofBanner
          proof={proof}
          cached={session.cached}
          outOfDate={session.outOfDate}
          logOpen={logOpen}
          onLog={() => setLogToggled(!logOpen)}
          onRerender={session.rerender}
        />
      )}
      {proof && logOpen && <ProofLog log={proof.log} />}
      <div className="min-h-0 flex-1 overflow-auto bg-bg p-4">
        {waiting && (
          <p data-testid={layoutTestIds.proofSteps} className="m-0 text-sm text-muted">
            {proof?.step ?? 'Asking for a proof'}…{' '}
            {proof?.progress != null && `${Math.round(proof.progress * 100)}%`}
          </p>
        )}
        {failed && (
          <ErrorState
            testId={layoutTestIds.proofError}
            title="The proof did not compile"
            detail={proof.error ?? 'See the log.'}
          />
        )}
        {proof?.status === 'succeeded' && proof.pdfUrl !== null && (
          <ProofPages pdfUrl={proof.pdfUrl} zoom={zoom} />
        )}
      </div>
    </div>
  );
}
