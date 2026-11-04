import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { statusLabel } from './sections';
import { statusColor } from './statusColor';

/** The status dot and its name at the end of a row. */
export function StatusLabel({ status }: { status: string }) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View style={styles.status}>
      <View style={[styles.dot, { backgroundColor: statusColor(theme, status) }]} />
      <Text style={styles.text}>{statusLabel(status)}</Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    status: { flexDirection: 'row', alignItems: 'center', gap: 5 },
    dot: { width: 6, height: 6, borderRadius: 3 },
    text: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
