import type { CitationStyle, PublicationSettings } from '@codex/core/api/settings';
import { citationStyles } from '@codex/core/features/settings/publicationDraft';
import { FieldRow } from './FieldRow';
import { SourceChip } from './SourceChip';
import { settingsTestIds } from './testIds';
import type { PublicationState } from './usePublication';

// In-text example and two bibliography entries per style; the book's text doesn't change.
const examples: Record<CitationStyle, { inText: string; entries: string[] }> = {
  chicago: {
    inText: '…makes it later.¹⁰ Note: Brooks, Mythical Man-Month, 25.',
    entries: [
      'Brooks, Frederick P., Jr. The Mythical Man-Month. Anniversary ed. Addison-Wesley, 1995.',
      'Conway, Melvin E. “How Do Committees Invent?” Datamation (1968).',
    ],
  },
  apa: {
    inText: '…makes it later (Brooks, 1995, p. 25).',
    entries: [
      'Brooks, F. P., Jr. (1995). The Mythical Man-Month (Anniversary ed.). Addison-Wesley.',
      'Conway, M. E. (1968). How do committees invent? Datamation.',
    ],
  },
  harvard: {
    inText: '…makes it later (Brooks, 1995, p. 25).',
    entries: [
      'Brooks, F.P. (1995) The Mythical Man-Month. Anniversary edn. Addison-Wesley.',
      'Conway, M.E. (1968) ‘How do committees invent?’, Datamation.',
    ],
  },
};

export function CitationStyleRow({
  settings,
  state,
}: {
  settings: PublicationSettings;
  state: PublicationState;
}) {
  const example = examples[settings.style.value];
  return (
    <FieldRow
      label="Citation style"
      chip={
        <SourceChip
          field="style"
          inSeries={settings.seriesName !== null}
          inherited={settings.style.inherited}
          onReset={state.reset}
        />
      }
      help="Used by the bibliography, the citation in each source’s panel, and print. Changing it updates every citation; the text of the book doesn’t change."
    >
      <div
        role="group"
        aria-label="Citation style"
        data-testid={settingsTestIds.citationStyle}
        className="inline-flex overflow-hidden rounded border border-border"
      >
        {citationStyles.map((style) => (
          <button
            key={style.code}
            type="button"
            data-testid={settingsTestIds.citationStyleOption(style.code)}
            aria-pressed={settings.style.value === style.code}
            onClick={() => void state.choose({ style: style.code })}
            className="h-8 cursor-pointer border-0 border-r border-border bg-surface px-3 text-base text-text last:border-r-0 aria-pressed:bg-accent-soft"
          >
            {style.label}
          </button>
        ))}
      </div>
      <div
        data-testid={settingsTestIds.citationExample}
        className="w-full rounded border border-border bg-bg p-2 text-sm text-muted"
      >
        <p className="m-0 text-text">{example.inText}</p>
        {example.entries.map((entry) => (
          <p key={entry} className="m-0 mt-1">
            {entry}
          </p>
        ))}
      </div>
    </FieldRow>
  );
}
