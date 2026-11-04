import { StyleSheet, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { progressWidth } from './sections';

/** The 3px bar under a book's meta line: words over target. */
export function ProgressBar({
  progressPct,
  testID,
}: {
  progressPct: number | null;
  testID?: string;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.track}>
      <View style={[styles.fill, { width: `${progressWidth(progressPct)}%` }]} />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    track: {
      height: 3,
      borderRadius: 2,
      backgroundColor: theme.colors['surface-2'],
      overflow: 'hidden',
      marginTop: 3,
    },
    fill: { height: '100%', borderRadius: 2, backgroundColor: theme.colors.accent },
  });
}
