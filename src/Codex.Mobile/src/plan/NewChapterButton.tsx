import { Pressable, StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { planTestIds } from './testIds';

/** The dashed `+ New chapter` at the foot of an open act. */
export function NewChapterButton({
  actId,
  busy,
  onPress,
}: {
  actId: string;
  busy: boolean;
  onPress: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <Pressable
      testID={planTestIds.newChapter(actId)}
      onPress={onPress}
      disabled={busy}
      accessibilityRole="button"
      accessibilityState={{ busy, disabled: busy }}
      style={[styles.button, busy && styles.busy]}
    >
      <Text style={styles.label}>{busy ? 'Adding…' : '+ New chapter'}</Text>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    button: {
      height: 34,
      marginHorizontal: theme.space[3],
      marginTop: theme.space[1],
      marginBottom: theme.space[2],
      alignItems: 'center',
      justifyContent: 'center',
      borderWidth: 1,
      borderStyle: 'dashed',
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
    },
    busy: { opacity: 0.6 },
    label: { fontSize: theme.text.sm, color: theme.colors.muted },
  });
}
