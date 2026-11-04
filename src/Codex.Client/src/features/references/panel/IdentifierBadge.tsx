import type { IdentifierCheck } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

const looks = {
  ok: { text: 'Verified', tone: 'text-ok' },
  redirect: { text: 'Redirects', tone: 'text-warn' },
  not_found: { text: 'Not found', tone: 'text-danger' },
  error: { text: 'Could not check', tone: 'text-danger' },
  checking: { text: 'Checking…', tone: 'text-accent' },
} as const;

// The result of the last ISBN, DOI or URL check, and for a redirect the button that takes the new address.
export function IdentifierBadge({
  check,
  busy,
  onAccept,
}: {
  check: IdentifierCheck;
  busy: boolean;
  onAccept: (redirectTo: string) => void;
}) {
  const look = looks[check.status];
  return (
    <span className="flex items-center gap-2 text-xs">
      <span
        data-testid={refsTestIds.verification(check.field)}
        data-status={check.status}
        className={`font-medium ${look.tone}`}
      >
        {look.text}
      </span>
      {check.status === 'redirect' && check.resolvedUrl !== null && (
        <button
          type="button"
          disabled={busy}
          title={check.resolvedUrl}
          data-testid={refsTestIds.updateAddress(check.field)}
          onClick={() => onAccept(check.resolvedUrl ?? '')}
          className="text-accent underline"
        >
          Update it
        </button>
      )}
    </span>
  );
}
