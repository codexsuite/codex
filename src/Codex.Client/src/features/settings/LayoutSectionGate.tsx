import { Link } from 'react-router-dom';
import { referencesLink } from '@codex/core/shell/links';
import { FieldRow } from './FieldRow';
import { settingsTestIds } from './testIds';

// Whether anything blocks print export: copyright risks in References (preflight issues are checked in Layout).
export function LayoutSectionGate({ bookId, riskCount }: { bookId: string; riskCount: number }) {
  return (
    <FieldRow
      label="Print export"
      help={
        riskCount > 0
          ? 'They come from References and the publisher rules above. Preflight in Layout checks the rest.'
          : 'Preflight in Layout checks images, directives and raw LaTeX.'
      }
    >
      <span
        data-testid={settingsTestIds.layoutGate}
        data-blocked={riskCount > 0}
        className={`text-sm ${riskCount > 0 ? 'text-danger' : 'text-ok'}`}
      >
        {riskCount > 0
          ? `${riskCount} copyright ${riskCount === 1 ? 'risk blocks' : 'risks block'} print export`
          : 'No copyright risks'}
      </span>
      {riskCount > 0 && (
        <Link to={referencesLink(bookId)} className="text-xs text-accent">
          Open References
        </Link>
      )}
    </FieldRow>
  );
}
