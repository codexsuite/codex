import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';

/** A muted line while a group's values load. */
export function LoadingNote({ text, testID }: { text: string; testID: string }) {
  const { styles } = useStyles(makeStyles);
  return (
    <Text testID={testID} style={styles.note}>
      {text}
    </Text>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    note: { padding: theme.space[4], fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
