import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { libraryTestIds } from './testIds';

/** A small line above the list when a refresh failed: the books already loaded stay on screen. */
export function RefreshFailedNotice() {
  const { styles } = useStyles(makeStyles);
  return (
    <Text testID={libraryTestIds.refreshFailed} accessibilityRole="alert" style={styles.notice}>
      {"Couldn't refresh. Showing the books loaded earlier."}
    </Text>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    notice: {
      paddingHorizontal: theme.space[4],
      paddingVertical: theme.space[2],
      fontSize: theme.text.xs,
      color: theme.colors.danger,
      backgroundColor: theme.colors.surface,
      borderBottomWidth: StyleSheet.hairlineWidth,
      borderBottomColor: theme.colors.border,
    },
  });
}
