import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { libraryTestIds } from './testIds';

/** The bar above the title: the avatar (the signed-in user's initials) that opens the Account sheet. */
export function LibraryTopBar({
  initials,
  onAccount,
}: {
  initials: string;
  onAccount: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.bar}>
      <Pressable
        testID={libraryTestIds.avatar}
        onPress={onAccount}
        accessibilityRole="button"
        accessibilityLabel="Account"
        hitSlop={8}
        style={styles.avatar}
      >
        <Text style={styles.initials}>{initials}</Text>
      </Pressable>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    bar: {
      height: theme.size.bar,
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'flex-end',
      paddingHorizontal: theme.space[3],
      backgroundColor: theme.colors.surface,
    },
    avatar: {
      width: 28,
      height: 28,
      borderRadius: 14,
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: theme.colors['accent-soft'],
    },
    initials: {
      fontSize: theme.text.xs,
      fontWeight: theme.weight.semibold,
      color: theme.colors.accent,
    },
  });
}
