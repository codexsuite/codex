import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { CategoryName, Theme } from '~/theme';

/** A Knowledge base entry on a scene card: a small square in the category colour and the name. */
export function EntryTag({
  name,
  category,
  testID,
}: {
  name: string;
  category: CategoryName | undefined;
  testID?: string;
}) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.tag}>
      <View
        style={[
          styles.square,
          {
            backgroundColor: category === undefined ? theme.colors.muted : theme.category[category],
          },
        ]}
      />
      <Text style={styles.name} numberOfLines={1}>
        {name}
      </Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    tag: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[1],
      paddingHorizontal: theme.space[2],
      height: 20,
      borderRadius: 10,
      backgroundColor: theme.colors['surface-2'],
    },
    square: { width: 8, height: 8, borderRadius: 2 },
    name: { fontSize: theme.text.xs, color: theme.colors.text },
  });
}
