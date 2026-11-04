import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { kbTestIds } from './testIds';

/** An alias with a × that removes it. */
export function AliasChip({ alias, onRemove }: { alias: string; onRemove?: () => void }) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={kbTestIds.alias(alias)} style={styles.chip}>
      <Text style={styles.label}>{alias}</Text>
      {onRemove !== undefined && (
        <Pressable
          testID={kbTestIds.aliasRemove(alias)}
          onPress={onRemove}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel={`Remove ${alias}`}
        >
          <Text style={styles.remove}>×</Text>
        </Pressable>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    chip: {
      height: 28,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[3],
      borderRadius: 14,
      backgroundColor: theme.colors['surface-2'],
    },
    label: { fontSize: theme.text.sm, fontWeight: theme.weight.medium, color: theme.colors.text },
    remove: { fontSize: theme.text.md, color: theme.colors.muted },
  });
}
