import { StyleSheet, View } from 'react-native';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { progressPercent } from './formatBytes';

export function DownloadProgressBar({
  receivedBytes,
  totalBytes,
  testID,
}: {
  receivedBytes: number;
  totalBytes: number;
  testID: string;
}) {
  const { styles } = useStyles(makeStyles);
  const percent = progressPercent(receivedBytes, totalBytes);
  return (
    <View
      testID={testID}
      accessibilityRole="progressbar"
      accessibilityValue={{ min: 0, max: 100, now: percent }}
      style={styles.track}
    >
      <View style={[styles.fill, { width: `${percent}%` }]} />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    track: {
      height: theme.space[1],
      borderRadius: theme.radius.xs,
      overflow: 'hidden',
      backgroundColor: theme.colors['surface-2'],
    },
    fill: { height: theme.space[1], backgroundColor: theme.colors.accent },
  });
}
