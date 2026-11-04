import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { LabelMark } from './LabelMark';

/** A label as the design draws it on a card: a dot in its tone and its name. */
export function LabelDot({
  name,
  color,
  testID,
}: {
  name: string;
  color: string | null;
  testID?: string;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={testID} style={styles.label}>
      <LabelMark color={color} />
      <Text style={styles.name} numberOfLines={1}>
        {name}
      </Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    label: { flexDirection: 'row', alignItems: 'center', gap: theme.space[1] },
    name: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
