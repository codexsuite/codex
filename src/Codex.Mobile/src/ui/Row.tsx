import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { Chip } from './Chip';
import { part, shellTestIds } from './testIds';

export interface RowProps {
  label: string;
  /** The current value, right-aligned and muted (`Writing`, `2,400 words`). */
  value?: string;
  /** A second muted line under the label. */
  detail?: string;
  /** A small chip after the label: `Series`, `Desktop`. */
  chip?: string;
  /** Tapping the row. A row without it is not a button and shows no chevron. */
  onPress?: () => void;
  /** Dims the row and ignores taps (a setting that only opens on desktop). */
  disabled?: boolean;
  /** Label in the danger colour (Archive book). */
  danger?: boolean;
  testID?: string;
}

export function Row({
  label,
  value,
  detail,
  chip,
  onPress,
  disabled = false,
  danger = false,
  testID = shellTestIds.row,
}: RowProps) {
  const { styles } = useStyles(makeStyles);
  const tappable = onPress !== undefined && !disabled;
  const content = (
    <>
      <View style={styles.body}>
        <View style={styles.labelLine}>
          <Text style={[styles.label, danger && styles.danger]} numberOfLines={1}>
            {label}
          </Text>
          {chip !== undefined && <Chip label={chip} testID={part(testID, 'chip')} />}
        </View>
        {detail !== undefined && (
          <Text style={styles.detail} numberOfLines={2}>
            {detail}
          </Text>
        )}
      </View>
      {value !== undefined && (
        <Text testID={part(testID, 'value')} style={styles.value} numberOfLines={1}>
          {value}
        </Text>
      )}
      {tappable && <Text style={styles.chevron}>{'›'}</Text>}
    </>
  );
  const style = [styles.row, disabled && styles.disabled];
  return tappable ? (
    <Pressable testID={testID} onPress={onPress} accessibilityRole="button" style={style}>
      {content}
    </Pressable>
  ) : (
    <View testID={testID} style={style}>
      {content}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: {
      minHeight: theme.size.row,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      paddingVertical: theme.space[2],
      paddingHorizontal: theme.space[4],
      backgroundColor: theme.colors.surface,
      borderBottomWidth: StyleSheet.hairlineWidth,
      borderBottomColor: theme.colors.border,
    },
    disabled: { opacity: 0.55 },
    body: { flex: 1, gap: 2 },
    labelLine: { flexDirection: 'row', alignItems: 'center', gap: theme.space[2] },
    label: {
      flexShrink: 1,
      fontSize: theme.text.base,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    danger: { color: theme.colors.danger },
    detail: { fontSize: theme.text.sm, color: theme.colors.muted },
    value: { maxWidth: 170, fontSize: theme.text.base, color: theme.colors.muted },
    chevron: { fontSize: theme.text.lg, color: theme.colors.muted },
  });
}
