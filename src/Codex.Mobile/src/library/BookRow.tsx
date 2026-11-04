import type { LibraryBook } from '@codex/core/api/types';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Cover } from '~/ui';
import { ProgressBar } from './ProgressBar';
import { bookMeta, bookOverline } from './sections';
import { StatusLabel } from './StatusLabel';
import { libraryTestIds } from './testIds';

/** One book in the Library: cover, `Book 003` overline in a series, title, words and edited, progress bar, status. */
export function BookRow({ book, onPress }: { book: LibraryBook; onPress: () => void }) {
  const { styles } = useStyles(makeStyles);
  const overline = bookOverline(book);
  return (
    <Pressable
      testID={libraryTestIds.book(book.id)}
      onPress={onPress}
      accessibilityRole="button"
      style={styles.row}
    >
      <Cover palette={book.coverPalette} title={book.title} imageUrl={book.coverUrl} />
      <View style={styles.body}>
        {overline !== null && <Text style={styles.overline}>{overline}</Text>}
        <Text style={styles.title} numberOfLines={1}>
          {book.title}
        </Text>
        <Text style={styles.meta} numberOfLines={1}>
          {bookMeta(book)}
        </Text>
        <ProgressBar progressPct={book.progressPct} />
      </View>
      <StatusLabel status={book.status} />
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    row: {
      minHeight: theme.size.row,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      paddingVertical: theme.space[2],
      paddingLeft: theme.space[4],
      paddingRight: theme.space[3],
      backgroundColor: theme.colors.surface,
      borderBottomWidth: StyleSheet.hairlineWidth,
      borderBottomColor: theme.colors.border,
    },
    body: { flex: 1, minWidth: 0, gap: 2 },
    overline: {
      fontSize: theme.text['2xs'],
      fontWeight: theme.weight.medium,
      color: theme.colors.muted,
    },
    title: { fontSize: theme.text.base, fontWeight: theme.weight.medium, color: theme.colors.text },
    meta: { fontSize: theme.text.xs, color: theme.colors.muted },
  });
}
