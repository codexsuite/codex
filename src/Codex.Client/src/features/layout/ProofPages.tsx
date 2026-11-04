import { useRef } from 'react';
import { layoutTestIds } from './testIds';
import { usePdfPages } from './usePdfPages';

interface ProofPagesProps {
  pdfUrl: string;
  zoom: number;
}

// The proof's pages: pdf.js draws them into canvases in the host, which React leaves alone.
export function ProofPages({ pdfUrl, zoom }: ProofPagesProps) {
  const host = useRef<HTMLDivElement>(null);
  const pdf = usePdfPages(host, pdfUrl, zoom);
  return (
    <div
      ref={host}
      data-testid={layoutTestIds.proofPages}
      data-status={pdf.status}
      data-pages={pdf.pages}
      className="layout-proof-pages"
    />
  );
}
