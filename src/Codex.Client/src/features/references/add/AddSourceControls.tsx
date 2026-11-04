import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { referencesLink } from '@codex/core/shell/links';
import { RefIcon } from '../RefIcon';
import { useDeepLink } from '../../../shell/useDeepLink';
import { refsTestIds } from '../testIds';
import { useReturnTrip } from '../useReturnTrip';
import { AddSourceForm } from './AddSourceForm';
import { ImportDialog } from './ImportDialog';

// `.btn` and `.btn.primary` of the reference screens.
const toolbarButton =
  'inline-flex h-[var(--control-height)] cursor-pointer items-center gap-[5px] rounded-sm border border-border bg-surface px-2.5 text-sm font-medium whitespace-nowrap text-text hover:bg-surface-2';
const primaryButton =
  'inline-flex h-[var(--control-height)] cursor-pointer items-center gap-[5px] rounded-sm border border-accent bg-accent px-2.5 text-sm font-medium whitespace-nowrap text-accent-fg hover:brightness-[1.07]';

// The toolbar's Add source and Import buttons. Add source is open exactly when the link carries `#new`, so the deep
// links from Write open it and closing it takes the hash away.
export function AddSourceControls({ bookId }: { bookId: string }) {
  const deepLink = useDeepLink();
  const navigate = useNavigate();
  const [importing, setImporting] = useState(false);
  const returning = useReturnTrip() ?? undefined;
  const link = deepLink.ok ? deepLink.link : null;
  const adding = link?.isNew === true;
  const goTo = (isNew: boolean) =>
    void navigate(referencesLink(bookId, { isNew, returnTo: link?.returnTo, sel: link?.sel }), {
      replace: true,
    });

  return (
    <>
      <div className="relative">
        <button
          type="button"
          aria-expanded={adding}
          data-testid={refsTestIds.addOpen}
          onClick={() => goTo(!adding)}
          className={primaryButton}
        >
          <RefIcon name="plus" />
          Add source
        </button>
        {adding && (
          <AddSourceForm bookId={bookId} onClose={() => goTo(false)} returning={returning} />
        )}
      </div>
      <div className="relative">
        <button
          type="button"
          aria-expanded={importing}
          data-testid={refsTestIds.addImportOpen}
          onClick={() => setImporting(!importing)}
          className={toolbarButton}
        >
          Import
        </button>
        {importing && <ImportDialog bookId={bookId} onClose={() => setImporting(false)} />}
      </div>
    </>
  );
}
