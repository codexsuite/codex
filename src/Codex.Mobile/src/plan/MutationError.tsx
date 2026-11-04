import { StyleSheet, Text } from 'react-native';
import { ApiRequestError } from '@codex/core/api/client';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';

/** A danger line for a failed write; nothing when there is no error. API errors say their own sentence. */
export function MutationError({
  error,
  fallback,
  testID,
}: {
  error: unknown;
  fallback: string;
  testID: string;
}) {
  const { styles } = useStyles(makeStyles);
  if (error === null || error === undefined) return null;
  return (
    <Text testID={testID} accessibilityRole="alert" style={styles.error}>
      {error instanceof ApiRequestError ? error.message : fallback}
    </Text>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
  });
}
