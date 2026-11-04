import type { LibraryBook } from '@codex/core/api/types';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Cover } from '~/ui';
import { ProgressBar } from './ProgressBar';
import { bookMeta } from './sections';
import { libraryTestIds } from './testIds';

/** The book last opened, with where it was left (`Write`, `Knowledge`); tapping it reopens that screen. */
export function ContinueCard({
  book,
  where,
  onPress,
}: {
  book: LibraryBook;
  where: string;
  onPress: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <Pressable
      testID={libraryTestIds.continueCard}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`Continue ${book.title} in ${where}`}
      style={styles.card}
    >
      <Cover palette={book.coverPalette} title={book.title} imageUrl={book.coverUrl} width={44} />
      <View style={styles.body}>
        <Text style={styles.overline}>{where}</Text>
        <Text style={styles.title} numberOfLines={1}>
          {book.title}
        </Text>
        <Text style={styles.meta} numberOfLines={1}>
          {bookMeta(book)}
        </Text>
        <ProgressBar progressPct={book.progressPct} />
      </View>
      <View style={styles.go}>
        <Text style={styles.goMark}>{'›'}</Text>
      </View>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    card: {
      marginHorizontal: theme.space[3],
      marginTop: 6,
      padding: theme.space[3],
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      backgroundColor: theme.colors.surface,
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
    },
    body: { flex: 1, minWidth: 0, gap: 3 },
    overline: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.semibold,
      letterSpacing: 0.6,
      textTransform: 'uppercase',
      color: theme.colors.accent,
    },
    title: { fontSize: theme.text.md, fontWeight: theme.weight.semibold, color: theme.colors.text },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
    go: {
      width: 36,
      height: 36,
      borderRadius: 18,
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: theme.colors.accent,
    },
    goMark: { fontSize: theme.text.lg, color: theme.colors['accent-fg'] },
  });
}
