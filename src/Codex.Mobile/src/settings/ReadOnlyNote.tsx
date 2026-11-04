import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { settingsTestIds } from './testIds';

/** The line a viewer or commenter sees above their read-only settings. */
export function ReadOnlyNote({ text }: { text: string }) {
  const { styles } = useStyles(makeStyles);
  return (
    <Text testID={settingsTestIds.readOnlyNote} style={styles.note}>
      {text}
    </Text>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    note: {
      margin: theme.space[4],
      padding: theme.space[3],
      fontSize: theme.text.sm,
      color: theme.colors.muted,
      backgroundColor: theme.colors['surface-2'],
      borderRadius: theme.radius.md,
    },
  });
}
