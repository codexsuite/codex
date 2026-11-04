import { StyleSheet, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { categoryColour } from './categoryColour';

/** The small coloured square of a category. */
export function CategorySquare({ colour, size = 12 }: { colour: string | null; size?: number }) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View
      style={[
        styles.square,
        { width: size, height: size, backgroundColor: categoryColour(theme, colour) },
      ]}
    />
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({ square: { borderRadius: theme.radius.xs } });
}
