import { StyleSheet, Text, View } from 'react-native';
import type { KbCategory } from '@codex/core/api/kb';
import { Chip, TextField } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { recordTestIds } from './testIds';

/** The name and category of the entry a scene recording becomes. */
export function NewEntryForm({
  name,
  onNameChange,
  categories,
  categoryId,
  onCategoryChange,
}: {
  name: string;
  onNameChange: (name: string) => void;
  categories: readonly KbCategory[];
  categoryId: string | null;
  onCategoryChange: (id: string) => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.box}>
      <TextField
        label="Entry name"
        value={name}
        onChangeText={onNameChange}
        placeholder="Name of the new entry"
        testID={recordTestIds.entryName}
      />
      <Text style={styles.caption}>Category</Text>
      {categories.length === 0 ? (
        <Text style={styles.caption}>
          This book has no categories yet. Add one in the Knowledge base.
        </Text>
      ) : (
        <View style={styles.row}>
          {categories.map((category) => (
            <Chip
              key={category.id}
              label={category.name}
              selected={category.id === categoryId}
              onPress={() => onCategoryChange(category.id)}
              testID={recordTestIds.category(category.id)}
            />
          ))}
        </View>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { gap: theme.space[2] },
    caption: { fontSize: theme.text.xs, color: theme.colors.muted },
    row: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
  });
}
