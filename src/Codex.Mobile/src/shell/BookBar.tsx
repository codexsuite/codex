import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useBookHeader } from '../book/queries';
import { useBookId } from '../book/useBookId';
import { bookSwitcherLink, libraryLink } from '../links';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { Cover } from '../ui/Cover';
import { part, shellTestIds } from '../ui/testIds';
import { SaveDot } from './SaveDot';

/** `Book 3 · Forsaken Gods`, the series name alone for an unnumbered book, `Standalone` outside a series. */
export function bookSubtitle(header: {
  seriesName: string | null;
  seriesNumber: number | null;
}): string {
  if (header.seriesName === null) return 'Standalone';
  return header.seriesNumber === null
    ? header.seriesName
    : `Book ${header.seriesNumber} · ${header.seriesName}`;
}

// TODO: the ⋯ button opens the book menu (rename, share, archive); it does nothing yet.
/** The bar at the top of a book's tab screens: back to the Library, the book, the book switcher, save status, ⋯. */
export function BookBar() {
  const { styles } = useStyles(makeStyles);
  const insets = useSafeAreaInsets();
  const bookId = useBookId();
  const book = useBookHeader(bookId).data;
  const id = shellTestIds.bookBar;

  return (
    <View style={[styles.wrap, { paddingTop: insets.top }]}>
      <View testID={id} style={styles.bar}>
        <Pressable
          testID={part(id, 'back')}
          onPress={() => router.dismissTo(libraryLink())}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Back to Library"
          style={styles.back}
        >
          <Text style={styles.backMark}>{'‹'}</Text>
        </Pressable>
        <Pressable
          testID={part(id, 'switcher')}
          onPress={() => router.push(bookSwitcherLink(bookId))}
          accessibilityRole="button"
          accessibilityLabel="Switch book"
          style={styles.book}
        >
          {book && (
            <Cover
              palette={book.coverPalette}
              title={book.title}
              imageUrl={book.coverUrl}
              width={22}
              testID={part(id, 'cover')}
            />
          )}
          <View style={styles.titles}>
            <Text testID={part(id, 'title')} style={styles.title} numberOfLines={1}>
              {book?.title ?? ' '}
            </Text>
            <Text testID={part(id, 'subtitle')} style={styles.subtitle} numberOfLines={1}>
              {book ? bookSubtitle(book) : ' '}
            </Text>
          </View>
          <Text style={styles.caret}>{'⌄'}</Text>
        </Pressable>
        <SaveDot />
        <Pressable
          testID={part(id, 'more')}
          onPress={() => {}}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Book menu"
          style={styles.more}
        >
          <Text style={styles.moreMark}>{'⋯'}</Text>
        </Pressable>
      </View>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    wrap: {
      backgroundColor: theme.colors.surface,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    bar: {
      height: theme.size.bar,
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: theme.space[2],
      gap: theme.space[1],
    },
    back: {
      width: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
    },
    backMark: { fontSize: 34, lineHeight: 36, color: theme.colors.accent },
    book: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: theme.space[2], minWidth: 0 },
    titles: { flexShrink: 1 },
    title: { fontSize: theme.text.md, fontWeight: theme.weight.semibold, color: theme.colors.text },
    subtitle: { fontSize: theme.text.xs, color: theme.colors.muted },
    caret: { fontSize: theme.text.md, color: theme.colors.muted },
    more: {
      width: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
    },
    moreMark: { fontSize: theme.text.lg, color: theme.colors.muted },
  });
}
