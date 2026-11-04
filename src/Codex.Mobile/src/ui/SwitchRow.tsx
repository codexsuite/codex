import { StyleSheet, Switch, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { part, shellTestIds } from './testIds';

export interface SwitchRowProps {
  label: string;
  description?: string;
  value: boolean;
  onValueChange: (value: boolean) => void;
  disabled?: boolean;
  testID?: string;
}

/** A row with a switch at its end (Always in context). The switch has the id `${testID}-switch`. */
export function SwitchRow({
  label,
  description,
  value,
  onValueChange,
  disabled = false,
  testID = shellTestIds.switchRow,
}: SwitchRowProps) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.row}>
      <View style={styles.body}>
        <Text style={styles.label}>{label}</Text>
        {description !== undefined && <Text style={styles.description}>{description}</Text>}
      </View>
      <Switch
        testID={part(testID, 'switch')}
        value={value}
        onValueChange={onValueChange}
        disabled={disabled}
        accessibilityLabel={label}
        trackColor={{ true: theme.colors.accent, false: theme.colors.border }}
      />
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
    body: { flex: 1, gap: 2 },
    label: { fontSize: theme.text.base, fontWeight: theme.weight.medium, color: theme.colors.text },
    description: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
