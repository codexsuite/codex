import type { Proof } from '@codex/core/api/proofs';
import { layoutTestIds } from './testIds';

const BUTTON = 'rounded-md border border-border px-2 py-1 text-xs';

interface ProofBannerProps {
  proof: Proof;
  cached: boolean;
  outOfDate: boolean;
  logOpen: boolean;
  onLog: () => void;
  onRerender: () => void;
}

// What the proof is: chapter only, the template version, and Download, Log and Re-render. An out-of-date proof says so.
export function ProofBanner({
  proof,
  cached,
  outOfDate,
  logOpen,
  onLog,
  onRerender,
}: ProofBannerProps) {
  return (
    <div
      data-testid={layoutTestIds.proofBanner}
      className="flex flex-wrap items-center gap-3 border-b border-border bg-surface-2 px-3 py-2 text-xs"
    >
      <span>
        Proof · chapter only, no contents or cross-references · {proof.templateSlug} v
        {proof.templateVersion} · {proof.trim}
      </span>
      {cached && (
        <span data-testid={layoutTestIds.proofCached} className="text-muted">
          cached
        </span>
      )}
      {outOfDate && (
        <span data-testid={layoutTestIds.proofOutOfDate} className="text-warn">
          Out of date: the chapter changed after this proof
        </span>
      )}
      <span className="grow" />
      {proof.pdfUrl !== null && (
        <a
          data-testid={layoutTestIds.proofDownload}
          className={`${BUTTON} text-accent`}
          href={proof.pdfUrl}
          download={`proof-${proof.id}.pdf`}
        >
          Download
        </a>
      )}
      <button
        type="button"
        data-testid={layoutTestIds.proofLogToggle}
        aria-pressed={logOpen}
        className={BUTTON}
        onClick={onLog}
      >
        Log
      </button>
      <button
        type="button"
        data-testid={layoutTestIds.proofRerender}
        className={BUTTON}
        onClick={onRerender}
      >
        Re-render
      </button>
    </div>
  );
}
