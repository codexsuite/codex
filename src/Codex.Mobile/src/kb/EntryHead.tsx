import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { CategorySquare } from './CategorySquare';
import { kbTestIds } from './testIds';

/** The category chip and the name. */
export function EntryHead({
  categoryName,
  colour,
  name,
}: {
  categoryName: string;
  colour: string | null;
  name: string;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.head}>
      <View testID={kbTestIds.entryCategory} style={styles.category}>
        <CategorySquare colour={colour} size={10} />
        <Text style={styles.categoryName}>{categoryName}</Text>
      </View>
      <Text testID={kbTestIds.entryName} accessibilityRole="header" style={styles.name}>
        {name}
      </Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    head: { gap: theme.space[2], paddingHorizontal: theme.space[4], paddingTop: theme.space[4] },
    category: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      alignSelf: 'flex-start',
      height: 28,
      paddingHorizontal: theme.space[3],
      borderRadius: 14,
      backgroundColor: theme.colors['surface-2'],
    },
    categoryName: {
      fontSize: theme.text.sm,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    name: { fontSize: theme.text.lg, fontWeight: theme.weight.semibold, color: theme.colors.text },
  });
}
