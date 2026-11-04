import { saveStatusStore, type SaveState } from '@codex/core/editing/saveStatusStore';
import { useSyncExternalStore } from 'react';
import { StyleSheet, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { shellTestIds } from '../ui/testIds';

/** The book bar's save-status dot, driven by the core's one save-status store (every autosave in the app reports to it). */
export function SaveDot() {
  const { theme, styles } = useStyles(makeStyles);
  const state = useSyncExternalStore(saveStatusStore.subscribe, saveStatusStore.getState);
  const color = {
    saved: theme.colors.ok,
    saving: theme.colors.info,
    unsaved: theme.colors.muted,
    conflict: theme.colors.danger,
    retry: theme.colors.danger,
    remoteChanged: theme.colors.warn,
    offline: theme.colors.warn,
  } satisfies Record<SaveState, string>;
  return (
    <View
      testID={shellTestIds.saveDot}
      accessibilityLabel={`Save status: ${state}`}
      style={[styles.dot, { backgroundColor: color[state] }]}
    />
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    dot: { width: 8, height: 8, borderRadius: 4, marginHorizontal: theme.space[2] },
  });
}
