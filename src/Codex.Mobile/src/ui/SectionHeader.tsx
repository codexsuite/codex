import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { part, shellTestIds } from './testIds';

export interface SectionHeaderProps {
  title: string;
  /** A muted count after the title (`12`). */
  count?: number;
  /** A trailing text button (`+ Alias`, a category's `+`). */
  action?: { label: string; onPress: () => void };
  testID?: string;
}

export function SectionHeader({
  title,
  count,
  action,
  testID = shellTestIds.sectionHeader,
}: SectionHeaderProps) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.header}>
      <Text accessibilityRole="header" style={styles.title}>
        {title}
      </Text>
      {count !== undefined && <Text style={styles.count}>{count}</Text>}
      <View style={styles.spacer} />
      {action !== undefined && (
        <Pressable
          testID={part(testID, 'action')}
          onPress={action.onPress}
          hitSlop={8}
          accessibilityRole="button"
        >
          <Text style={styles.action}>{action.label}</Text>
        </Pressable>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[4],
      paddingBottom: theme.space[1],
    },
    title: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      letterSpacing: 0.6,
      textTransform: 'uppercase',
      color: theme.colors.muted,
    },
    count: { fontSize: theme.text.xs, color: theme.colors.muted },
    spacer: { flex: 1 },
    action: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.medium,
      color: theme.colors.accent,
    },
  });
}
