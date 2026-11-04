import { layoutTestIds } from './testIds';

// The compile log: pandoc's then Tectonic's output, with LaTeX's own messages in it.
export function ProofLog({ log }: { log: string | null }) {
  return (
    <pre
      data-testid={layoutTestIds.proofLog}
      className="m-0 max-h-64 overflow-auto border-b border-border bg-surface-2 p-3 text-xs"
    >
      {log === null || log === '' ? 'The compile left no log.' : log}
    </pre>
  );
}
