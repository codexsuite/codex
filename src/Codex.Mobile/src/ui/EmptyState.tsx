import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { Button } from './Button';
import { part, shellTestIds } from './testIds';

export interface EmptyStateProps {
  title: string;
  message?: string;
  action?: { label: string; onPress: () => void };
  testID?: string;
}

export function EmptyState({
  title,
  message,
  action,
  testID = shellTestIds.emptyState,
}: EmptyStateProps) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.box}>
      <Text style={styles.title}>{title}</Text>
      {message !== undefined && <Text style={styles.message}>{message}</Text>}
      {action !== undefined && (
        <Button label={action.label} onPress={action.onPress} testID={part(testID, 'action')} />
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { alignItems: 'center', gap: theme.space[2], padding: theme.space[5] },
    title: {
      fontSize: theme.text.md,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
      textAlign: 'center',
    },
    message: { fontSize: theme.text.base, color: theme.colors.muted, textAlign: 'center' },
  });
}
