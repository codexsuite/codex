import { StyleSheet, Text, View } from 'react-native';
import type { Kb } from '@codex/core/api/kb';
import type { UseQueryResult } from '@tanstack/react-query';
import type { RecordTarget } from '~/links';
import { ErrorState, TextField } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { EntryPicker } from './EntryPicker';
import { LandingChoices, type LandingMode } from './LandingChoices';
import { NewEntryForm } from './NewEntryForm';
import { recordTestIds } from './testIds';

export interface ReviewDraft {
  transcript: string;
  mode: LandingMode;
  entryName: string;
  categoryId: string | null;
  pickedEntryId: string | null;
}

/** After transcription: the editable text, then what it becomes (and the form that choice needs). */
export function TranscriptReview({
  target,
  kb,
  draft,
  onChange,
  saveError,
}: {
  target: RecordTarget;
  kb: UseQueryResult<Kb>;
  draft: ReviewDraft;
  onChange: (patch: Partial<ReviewDraft>) => void;
  saveError: string | null;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.box}>
      <TextField
        label="Transcript"
        value={draft.transcript}
        onChangeText={(transcript) => onChange({ transcript })}
        multiline
        minLines={5}
        placeholder="Nothing was heard. Type the text here."
        testID={recordTestIds.transcript}
      />
      <LandingChoices target={target} mode={draft.mode} onChange={(mode) => onChange({ mode })} />
      {draft.mode !== 'add-to-entry' && kb.isPending && (
        <Text testID={recordTestIds.kbLoading} style={styles.muted}>
          Loading the Knowledge base…
        </Text>
      )}
      {draft.mode !== 'add-to-entry' && kb.isError && (
        <ErrorState
          error={kb.error}
          fallback="The Knowledge base could not be loaded."
          onRetry={() => void kb.refetch()}
          testID={recordTestIds.kbError}
        />
      )}
      {draft.mode === 'new-entry' && kb.isSuccess && (
        <NewEntryForm
          name={draft.entryName}
          onNameChange={(entryName) => onChange({ entryName })}
          categories={kb.data.categories}
          categoryId={draft.categoryId ?? kb.data.categories[0]?.id ?? null}
          onCategoryChange={(categoryId) => onChange({ categoryId })}
        />
      )}
      {draft.mode === 'add-to-an-entry' && kb.isSuccess && (
        <EntryPicker
          entries={kb.data.entries}
          pickedId={draft.pickedEntryId}
          onPick={(pickedEntryId) => onChange({ pickedEntryId })}
        />
      )}
      {saveError !== null && (
        <Text testID={recordTestIds.saveError} accessibilityRole="alert" style={styles.error}>
          {saveError}
        </Text>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { gap: theme.space[4], padding: theme.space[4] },
    error: { fontSize: theme.text.base, color: theme.colors.danger },
    muted: { fontSize: theme.text.base, color: theme.colors.muted },
  });
}
