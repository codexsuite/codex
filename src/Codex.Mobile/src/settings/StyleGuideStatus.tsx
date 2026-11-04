import type { SaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { useSyncExternalStore } from 'react';
import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { saveStatusText } from './saveStatusText';
import { settingsTestIds } from './testIds';

/** The editor's own save status (the book bar's dot shows the worst across the book). */
export function StyleGuideStatus({
  error,
  store,
}: {
  error: string | null;
  store: SaveStatusStore;
}) {
  const { styles } = useStyles(makeStyles);
  const state = useSyncExternalStore(store.subscribe, store.getState);
  return (
    <>
      {error !== null && state !== 'offline' && (
        <Text
          testID={settingsTestIds.styleGuideError}
          accessibilityRole="alert"
          style={styles.error}
        >
          {error}
        </Text>
      )}
      <Text testID={settingsTestIds.styleGuideStatus} style={styles.status}>
        {saveStatusText[state]}
      </Text>
    </>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    status: { fontSize: theme.text.sm, color: theme.colors.muted },
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
  });
}
