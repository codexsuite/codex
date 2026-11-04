import type { OutlineScene } from '@codex/core/api/structure';
import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Button, Chip, TextField } from '~/ui';
import { FieldLabel } from './FieldLabel';
import { LabelMark } from './LabelMark';
import { MutationError } from './MutationError';
import { planTestIds } from './testIds';
import { useSceneLabels } from './useSceneLabels';

/**
 * The scene's labels as chips (tap to take one off) and `+ Label`, which opens the book's label set to pick from and a
 * field to make a new label; every change saves at once, as the desktop picker does.
 */
export function SceneLabelsField({
  bookId,
  scene,
  editable,
}: {
  bookId: string;
  scene: OutlineScene;
  editable: boolean;
}) {
  const { styles } = useStyles(makeStyles);
  const { labels, toggle, save, create: createLabel } = useSceneLabels(bookId, scene);
  const [picking, setPicking] = useState(false);
  const [draft, setDraft] = useState('');
  const selected = scene.labels.map((label) => label.id);
  const create = () => {
    const name = draft.trim();
    if (name === '') return;
    createLabel.mutate(name, { onSuccess: () => setDraft('') });
  };

  return (
    <View style={styles.field}>
      <FieldLabel>Labels</FieldLabel>
      <View style={styles.chips}>
        {scene.labels.map((label) => (
          <Chip
            key={label.id}
            testID={planTestIds.sheetLabel(label.id)}
            label={editable ? `${label.name} ×` : label.name}
            leading={<LabelMark color={label.color} />}
            onPress={editable ? () => toggle(label.id) : undefined}
          />
        ))}
        {editable && (
          <Chip
            testID={planTestIds.sheetLabelAdd}
            label="+ Label"
            selected={picking}
            onPress={() => setPicking((p) => !p)}
          />
        )}
      </View>
      {editable && picking && (
        <View testID={planTestIds.sheetLabelPicker} style={styles.picker}>
          {labels.isError && (
            <MutationError
              error={labels.error}
              fallback="Couldn't load the labels."
              testID={planTestIds.sheetError}
            />
          )}
          {labels.isPending && <Text style={styles.hint}>Loading labels…</Text>}
          <View style={styles.chips}>
            {labels.data?.map((label) => (
              <Chip
                key={label.id}
                testID={planTestIds.sheetLabelOption(label.id)}
                label={label.name}
                leading={<LabelMark color={label.color} />}
                selected={selected.includes(label.id)}
                onPress={() => toggle(label.id)}
              />
            ))}
          </View>
          <TextField
            testID={planTestIds.sheetLabelNew}
            placeholder="New label"
            value={draft}
            onChangeText={setDraft}
            onSubmitEditing={create}
            returnKeyType="done"
            autoCapitalize="none"
          />
          <Button
            testID={planTestIds.sheetLabelCreate}
            label="Create label"
            onPress={create}
            disabled={draft.trim() === ''}
            busy={createLabel.isPending}
          />
        </View>
      )}
      <MutationError
        error={save.error ?? createLabel.error}
        fallback="Couldn't save the labels."
        testID={`${planTestIds.sheetError}-labels`}
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    field: { gap: theme.space[2] },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
    picker: {
      gap: theme.space[2],
      padding: theme.space[3],
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors.bg,
    },
    hint: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
