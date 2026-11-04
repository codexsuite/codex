import { MarkdownSurface } from '../../prose';
import type { BookSettings } from '@codex/core/api/settings';
import { wordCount } from '@codex/core/features/settings/bookDraft';
import type { BookDraftState } from './useBookDraft';
import { FieldRow } from './FieldRow';
import { SettingsCard } from './SettingsCard';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';

// The book's style guide with its word count; in a series the series guide sits under it, read-only.
export function WritingSection({
  settings,
  state,
}: {
  settings: BookSettings;
  state: BookDraftState;
}) {
  const { draft, set, flush } = state;
  const words = wordCount(draft.styleGuide);
  // The guide is an editor, not a form control, so the page's disabled fieldset doesn't reach it (#80).
  const { canEdit } = useSettingsAccess(settings.id);
  return (
    <SettingsCard
      section="writing"
      title="Writing"
      blurb="Sent with every AI draft from this book, with the beat, its context and the scene so far."
    >
      <FieldRow
        label="Style guide"
        help={
          <>
            {words > 0 && (
              <>
                <span data-testid={settingsTestIds.guideWords}>
                  {words === 1 ? '1 word' : `${words} words`}
                </span>
                {' · '}
              </>
            )}
            Keep it short: it’s sent with every generation and counts against the model’s context.
            It doesn’t change text already written.
          </>
        }
      >
        <div
          data-testid={settingsTestIds.guide}
          aria-disabled={!canEdit}
          onBlur={(e) => {
            if (!e.currentTarget.contains(e.relatedTarget)) flush();
          }}
          className="min-h-[104px] w-full rounded-sm border border-border bg-bg px-[9px] py-[7px] text-sm leading-normal text-text focus-within:border-transparent focus-within:outline-2 focus-within:-outline-offset-1 focus-within:outline-accent"
        >
          <MarkdownSurface
            surface="style-guide"
            variant="prose"
            value={draft.styleGuide}
            onChange={(markdown) => set({ styleGuide: markdown })}
            readOnly={!canEdit}
            placeholder="Voice, tense, point of view, spelling, words to avoid…"
          />
        </div>
        {settings.seriesGuide !== null && (
          <div
            data-testid={settingsTestIds.seriesGuide}
            className="w-full rounded border border-border bg-bg p-2 text-sm text-muted"
          >
            <span className="mb-1 flex items-center gap-1 text-2xs uppercase tracking-wide">
              <span className="rounded bg-border px-1 text-text">Series</span>
              {settings.seriesName} style guide, also sent
            </span>
            {settings.seriesGuide}
          </div>
        )}
      </FieldRow>
    </SettingsCard>
  );
}
