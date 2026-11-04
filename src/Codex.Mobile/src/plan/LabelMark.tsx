import { StyleSheet, View } from 'react-native';
import { useTheme } from '~/theme';
import { toneOf } from './labelTone';

/** A label's dot: filled in its tone, hollow when the colour is none of the four. */
export function LabelMark({ color }: { color: string | null }) {
  const theme = useTheme();
  const tone = toneOf(color);
  return (
    <View
      style={[
        styles.dot,
        tone === null
          ? { borderWidth: 1, borderColor: theme.colors.muted }
          : { backgroundColor: theme.colors[tone] },
      ]}
    />
  );
}

const styles = StyleSheet.create({ dot: { width: 6, height: 6, borderRadius: 3 } });
