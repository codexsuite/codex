import { StyleSheet, Text, View } from 'react-native';
import { ApiRequestError } from '@codex/core/api/client';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { Button } from './Button';
import { part, shellTestIds } from './testIds';

export interface ErrorStateProps {
  /** The error from a failed query: an API error shows its own message, anything else the `fallback`. */
  error: unknown;
  /** What could not be loaded, as a sentence: `Couldn't load your library.` */
  fallback: string;
  onRetry: () => void;
  testID?: string;
}

/** A load error with a Retry button. The message has the id `testID`, the button `${testID}-retry`. */
export function ErrorState({
  error,
  fallback,
  onRetry,
  testID = shellTestIds.errorState,
}: ErrorStateProps) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.box}>
      <Text testID={testID} accessibilityRole="alert" style={styles.message}>
        {error instanceof ApiRequestError ? error.message : fallback}
      </Text>
      <Button label="Retry" onPress={onRetry} testID={part(testID, 'retry')} />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { alignItems: 'center', gap: theme.space[3], padding: theme.space[5] },
    message: { fontSize: theme.text.base, color: theme.colors.danger, textAlign: 'center' },
  });
}
