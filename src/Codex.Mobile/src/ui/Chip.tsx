import type { ReactNode } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { shellTestIds } from './testIds';

export interface ChipProps {
  label: string;
  /** The accent-soft look (a selected filter, the Series marker). */
  selected?: boolean;
  /** Makes the chip a button (a category filter). */
  onPress?: () => void;
  /** A small trailing count (`Characters 12`). */
  count?: number;
  /** Drawn before the label (a colour dot). */
  leading?: ReactNode;
  testID?: string;
}

export function Chip({
  label,
  selected = false,
  onPress,
  count,
  leading,
  testID = shellTestIds.chip,
}: ChipProps) {
  const { styles } = useStyles(makeStyles);
  const body = (
    <>
      {leading}
      <Text style={styles.label}>{label}</Text>
      {count !== undefined && <Text style={styles.count}>{count}</Text>}
    </>
  );
  if (onPress === undefined) {
    return (
      <View testID={testID} style={[styles.chip, selected && styles.selected]}>
        {body}
      </View>
    );
  }
  return (
    <Pressable
      testID={testID}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityState={{ selected }}
      style={[styles.chip, selected && styles.selected]}
    >
      {body}
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    chip: {
      height: 28,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[1],
      paddingHorizontal: theme.space[3],
      borderRadius: 14,
      backgroundColor: theme.colors['surface-2'],
      alignSelf: 'flex-start',
    },
    selected: { backgroundColor: theme.colors['accent-soft'] },
    label: { fontSize: theme.text.sm, fontWeight: theme.weight.medium, color: theme.colors.text },
    count: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
