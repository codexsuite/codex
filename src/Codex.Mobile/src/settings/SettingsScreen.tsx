import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { useBookSettings } from '~/book/queries';
import { useBookId } from '~/book/useBookId';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { ErrorState } from '~/ui';
import { readOnlyNote } from './access';
import { ArchiveGroup } from './ArchiveGroup';
import { BookGroup } from './BookGroup';
import { CitationsGroup } from './CitationsGroup';
import { LayoutGroup } from './LayoutGroup';
import { LoadingNote } from './LoadingNote';
import { ReadOnlyNote } from './ReadOnlyNote';
import { SeriesGroup } from './SeriesGroup';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';
import { useLayoutSettings, usePublicationSettings } from './useSettingsQueries';
import { useSettingsMutations } from './useSettingsMutations';
import { WritingGroup } from './WritingGroup';

/** The Settings tab: grouped rows in the desktop section order (the book bar above is the shell's). */
export function SettingsScreen() {
  const { styles } = useStyles(makeStyles);
  const bookId = useBookId();
  const settings = useBookSettings(bookId);
  const publication = usePublicationSettings(bookId);
  const layout = useLayoutSettings(bookId);
  const access = useSettingsAccess(bookId);
  const save = useSettingsMutations(bookId);

  if (settings.isPending) {
    return (
      <View testID={settingsTestIds.screen} style={styles.fill}>
        <LoadingNote testID={settingsTestIds.loading} text="Loading settings…" />
      </View>
    );
  }
  if (settings.isError) {
    return (
      <View testID={settingsTestIds.screen} style={styles.fill}>
        <ErrorState
          testID={settingsTestIds.error}
          error={settings.error}
          fallback="Couldn't load the settings."
          onRetry={() => void settings.refetch()}
        />
      </View>
    );
  }

  const book = settings.data;
  const note = readOnlyNote(access, access.ownerName, book.title);
  return (
    <ScrollView
      testID={settingsTestIds.screen}
      style={styles.fill}
      contentContainerStyle={styles.content}
    >
      {note !== null && <ReadOnlyNote text={note} />}
      <BookGroup settings={book} access={access} save={save} />
      <SeriesGroup settings={book} access={access} save={save} />
      <WritingGroup settings={book} />
      {publication.data ? (
        <CitationsGroup publication={publication.data} access={access} save={save} />
      ) : (
        <LoadingNote
          testID={settingsTestIds.row('citations-loading')}
          text={
            publication.isError
              ? "Couldn't load citations and permissions."
              : 'Loading citations and permissions…'
          }
        />
      )}
      {layout.data ? (
        <LayoutGroup layout={layout.data} />
      ) : (
        <LoadingNote
          testID={settingsTestIds.row('layout-loading')}
          text={layout.isError ? "Couldn't load layout and print." : 'Loading layout and print…'}
        />
      )}
      <ArchiveGroup settings={book} access={access} save={save} ownerName={access.ownerName} />
      {book.seriesName !== null && (
        <Text testID={settingsTestIds.seriesFootnote} style={styles.foot}>
          A value marked Series comes from {book.seriesName}; changing it here applies to this book
          only.
        </Text>
      )}
    </ScrollView>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    fill: { flex: 1, backgroundColor: theme.colors.bg },
    content: { paddingBottom: theme.space[5] },
    foot: {
      padding: theme.space[4],
      fontSize: theme.text.sm,
      color: theme.colors.muted,
    },
  });
}
