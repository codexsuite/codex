import { Link } from 'react-router-dom';
import type { PublicationSettings } from '@codex/core/api/settings';
import { referencesLink } from '@codex/core/shell/links';
import { CitationStyleRow } from './CitationStyleRow';
import { FieldRow } from './FieldRow';
import { LetterPreview } from './LetterPreview';
import { LetterRows } from './LetterRows';
import { QuoteLimitRow } from './QuoteLimitRow';
import { SettingsCard } from './SettingsCard';
import { settingsTestIds } from './testIds';
import { usePublication } from './usePublication';

const fixedStatus = 'text-2xs uppercase tracking-wide';

// Citation style, publisher rules (the quote limit with its live effect on References) and the permission-letter
// details. In a series book each value carries a Series / This book × chip.
export function CitationsSection({
  settings,
  title,
}: {
  settings: PublicationSettings;
  title: string;
}) {
  const state = usePublication(settings);
  return (
    <SettingsCard
      section="citations"
      title="Citations & permissions"
      blurb={
        settings.seriesName === null
          ? 'How References cites sources, which quotes need permission, and what permission letters say.'
          : `How References cites sources, which quotes need permission, and what permission letters say. Set on ${settings.seriesName} for every book in it; a value you change here is for this book only.`
      }
    >
      <div className="flex justify-end pt-2">
        <Link to={referencesLink(settings.bookId)} className="text-xs text-accent">
          References
        </Link>
      </div>
      {state.error && (
        <p
          data-testid={settingsTestIds.publicationError}
          role="alert"
          className="m-0 py-2 text-sm text-danger"
        >
          {state.error}
        </p>
      )}
      <CitationStyleRow settings={settings} state={state} />
      <div id="rules" className="scroll-mt-14 min-[860px]:scroll-mt-4">
        <h3 className="m-0 pt-3 text-base font-semibold text-text">Publisher rules</h3>
        <QuoteLimitRow settings={settings} state={state} />
        <FieldRow
          label="Lyrics and poetry"
          help="Song lyrics and poems need permission for any length, unless the licence allows reuse or the work is in the public domain."
        >
          <span className={`${fixedStatus} text-danger`}>Always ask</span>
        </FieldRow>
        <FieldRow
          label="Figures and images"
          help="A reproduced figure, table or image needs permission unless its licence allows reuse (for example CC BY)."
        >
          <span className={`${fixedStatus} text-warn`}>Ask unless licensed</span>
        </FieldRow>
      </div>
      <div id="letters" className="scroll-mt-14 min-[860px]:scroll-mt-4">
        <h3 className="m-0 pt-3 text-base font-semibold text-text">Permission letters</h3>
        <LetterRows settings={settings} state={state} />
        <LetterPreview title={title} fields={state.fields} formats={settings.formats.value} />
      </div>
    </SettingsCard>
  );
}
