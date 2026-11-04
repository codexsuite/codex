import { StyleSheet, View } from 'react-native';
import { useBookSettings } from '~/book/queries';
import { useBookId } from '~/book/useBookId';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { ErrorState, PushBar } from '~/ui';
import { LoadingNote } from './LoadingNote';
import { StyleGuideEditor } from './StyleGuideEditor';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';

/** Loads the settings, then hands the saved text to the editor (which keeps its own copy while you type). */
export function StyleGuideScreen() {
  const { styles } = useStyles(makeStyles);
  const bookId = useBookId();
  const settings = useBookSettings(bookId);
  const access = useSettingsAccess(bookId);

  if (settings.data) {
    return <StyleGuideEditor settings={settings.data} canEdit={access.canEdit} />;
  }
  return (
    <View testID={settingsTestIds.styleGuideScreen} style={styles.fill}>
      <PushBar parent="Settings" title="Style guide" />
      {settings.isError ? (
        <ErrorState
          testID={settingsTestIds.error}
          error={settings.error}
          fallback="Couldn't load the style guide."
          onRetry={() => void settings.refetch()}
        />
      ) : (
        <LoadingNote testID={settingsTestIds.loading} text="Loading the style guide…" />
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({ fill: { flex: 1, backgroundColor: theme.colors.bg } });
}
