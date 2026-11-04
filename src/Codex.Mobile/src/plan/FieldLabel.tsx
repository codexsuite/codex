import { StyleSheet, Text } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';

/** The small caps caption over a sheet field (`Labels`, `Knowledge base`). */
export function FieldLabel({ children }: { children: string }) {
  const { styles } = useStyles(makeStyles);
  return <Text style={styles.label}>{children}</Text>;
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    label: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      color: theme.colors.muted,
      textTransform: 'uppercase',
    },
  });
}
