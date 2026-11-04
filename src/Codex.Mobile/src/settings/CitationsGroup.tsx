import { useState } from 'react';
import type {
  CitationStyle,
  PublicationField,
  PublicationSettings,
  PublicationValue,
} from '@codex/core/api/settings';
import { citationStyles, territories } from '@codex/core/features/settings/publicationDraft';
import { Row, SectionHeader } from '~/ui';
import { ChoiceSheet } from './ChoiceSheet';
import type { GroupProps } from './BookGroup';
import {
  citationStyleLabel,
  countText,
  formatsText,
  parseWholeNumber,
  publisherText,
  quoteLimitText,
  textOrNotSet,
} from './fieldValues';
import { FormatsSheet } from './FormatsSheet';
import { settingsTestIds } from './testIds';
import { TextFieldSheet } from './TextFieldSheet';

type Editing = PublicationField | null;

/**
 * Citation style, publisher rules and the permission-letter details. In a series book a value that comes from the
 * series carries the Series chip; a value this book overrides can go back to the series' (Use series value).
 */
export function CitationsGroup({
  publication,
  access,
  save,
}: Pick<GroupProps, 'access' | 'save'> & { publication: PublicationSettings }) {
  const [editing, setEditing] = useState<Editing>(null);
  const close = () => setEditing(null);
  const inSeries = publication.seriesName !== null;
  const open = (field: PublicationField) => (access.canEdit ? () => setEditing(field) : undefined);
  const chipOf = (value: PublicationValue<unknown>) => (value.inherited ? 'Series' : undefined);
  const back = (field: PublicationField, value: PublicationValue<unknown>) =>
    inSeries && !value.inherited
      ? { label: 'Use series value', onPress: () => save.resetPublication(field) }
      : undefined;
  const row = settingsTestIds.row;
  const wholeNumber = (text: string, min: number) => {
    const parsed = parseWholeNumber(text, { allowBlank: min === 0, min });
    return parsed.ok ? null : parsed.message;
  };

  return (
    <>
      <SectionHeader title="Citations & permissions" />
      <Row
        testID={row('citation-style')}
        label="Citation style"
        value={citationStyleLabel(publication.style.value)}
        chip={chipOf(publication.style)}
        onPress={open('style')}
      />
      <Row
        testID={row('quote-limit')}
        label="Publisher rules"
        value={quoteLimitText(publication.quoteWordLimit.value)}
        chip={chipOf(publication.quoteWordLimit)}
        onPress={open('quoteWordLimit')}
      />
      <Row
        testID={row('publisher')}
        label="Publisher"
        value={publisherText(publication.publisher.value)}
        chip={chipOf(publication.publisher)}
        onPress={open('publisher')}
      />
      <Row
        testID={row('year')}
        label="Planned year"
        value={textOrNotSet(
          publication.plannedYear.value === null ? null : String(publication.plannedYear.value),
        )}
        chip={chipOf(publication.plannedYear)}
        onPress={open('plannedYear')}
      />
      <Row
        testID={row('formats')}
        label="Formats"
        value={formatsText(publication.formats.value)}
        chip={chipOf(publication.formats)}
        onPress={open('formats')}
      />
      <Row
        testID={row('print-run')}
        label="First print run"
        value={countText(publication.printRun.value)}
        chip={chipOf(publication.printRun)}
        onPress={open('printRun')}
      />
      <Row
        testID={row('territory')}
        label="Territory"
        value={textOrNotSet(publication.territory.value)}
        chip={chipOf(publication.territory)}
        onPress={open('territory')}
      />

      {editing === 'style' && (
        <ChoiceSheet<CitationStyle>
          title="Citation style"
          choices={citationStyles.map((s) => ({ value: s.code, label: s.label }))}
          selected={publication.style.value}
          onSave={(style) => save.savePublication({ style })}
          onClose={close}
          secondary={back('style', publication.style)}
        />
      )}
      {editing === 'quoteWordLimit' && (
        <TextFieldSheet
          title="Publisher rules"
          label="Quote word limit"
          initial={String(publication.quoteWordLimit.value)}
          keyboardType="number-pad"
          hint="Prose quoted beyond this many words needs permission. References shows what is at risk."
          validate={(text) => wholeNumber(text, 1)}
          onSave={(text) =>
            save.savePublication({ quoteWordLimit: Number(text.trim().replace(/,/g, '')) })
          }
          onClose={close}
          secondary={back('quoteWordLimit', publication.quoteWordLimit)}
        />
      )}
      {editing === 'publisher' && (
        <TextFieldSheet
          title="Publisher"
          label="Publisher"
          initial={publication.publisher.value ?? ''}
          hint="Leave it blank if you publish it yourself."
          validate={() => null}
          onSave={(text) => save.savePublication({ publisher: text })}
          onClose={close}
          secondary={back('publisher', publication.publisher)}
        />
      )}
      {editing === 'plannedYear' && (
        <TextFieldSheet
          title="Planned year"
          label="Year"
          initial={
            publication.plannedYear.value === null ? '' : String(publication.plannedYear.value)
          }
          keyboardType="number-pad"
          hint="Leave it blank to clear it."
          validate={(text) => wholeNumber(text, 0)}
          onSave={(text) => save.savePublication({ plannedYear: Number(text.trim() || '0') })}
          onClose={close}
          secondary={back('plannedYear', publication.plannedYear)}
        />
      )}
      {editing === 'formats' && (
        <FormatsSheet
          selected={publication.formats.value}
          onSave={(formats) => save.savePublication({ formats })}
          onClose={close}
          secondary={back('formats', publication.formats)}
        />
      )}
      {editing === 'printRun' && (
        <TextFieldSheet
          title="First print run"
          label="Copies"
          initial={publication.printRun.value === null ? '' : String(publication.printRun.value)}
          keyboardType="number-pad"
          hint="Leave it blank to clear it."
          validate={(text) => wholeNumber(text, 0)}
          onSave={(text) =>
            save.savePublication({ printRun: Number(text.trim().replace(/,/g, '') || '0') })
          }
          onClose={close}
          secondary={back('printRun', publication.printRun)}
        />
      )}
      {editing === 'territory' && (
        <TextFieldSheet
          title="Territory"
          label="Territory"
          initial={publication.territory.value ?? ''}
          suggestions={territories}
          validate={(text) => (text.trim() === '' ? 'Enter a territory.' : null)}
          onSave={(text) => save.savePublication({ territory: text })}
          onClose={close}
          secondary={back('territory', publication.territory)}
        />
      )}
    </>
  );
}
