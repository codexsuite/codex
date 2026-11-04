import { router } from 'expo-router';
import { KeyboardAvoidingView, ScrollView, StyleSheet, Text, View } from 'react-native';
import { wordCount } from '@codex/core/features/settings/bookDraft';
import type { BookSettings } from '@codex/core/api/settings';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { PushBar, TextField } from '~/ui';
import { StyleGuideStatus } from './StyleGuideStatus';
import { settingsTestIds } from './testIds';
import { useStyleGuideEditor } from './useStyleGuideEditor';

/** The pushed editor: the book's style guide as long text, saved as the desktop saves it, with Done. */
export function StyleGuideEditor({
  settings,
  canEdit,
}: {
  settings: BookSettings;
  canEdit: boolean;
}) {
  const { styles } = useStyles(makeStyles);
  const editor = useStyleGuideEditor(settings.id, settings.styleGuide ?? '');
  const words = wordCount(editor.text);

  const leave = async () => {
    if (!canEdit || (await editor.flush())) router.back();
  };

  return (
    <View testID={settingsTestIds.styleGuideScreen} style={styles.fill}>
      <PushBar
        parent="Settings"
        title="Style guide"
        onBack={() => void leave()}
        action={canEdit ? { label: 'Done', onPress: () => void leave() } : undefined}
      />
      <KeyboardAvoidingView behavior="padding" style={styles.fill}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <TextField
            testID={settingsTestIds.styleGuideField}
            label="Style guide"
            value={editor.text}
            onChangeText={editor.setText}
            multiline
            minLines={10}
            editable={canEdit}
            placeholder="Voice, tense, point of view, spelling, words to avoid…"
          />
          {canEdit && <StyleGuideStatus error={editor.error} store={editor.store} />}
          <Text testID={settingsTestIds.styleGuideWords} style={styles.muted}>
            {words === 1 ? '1 word' : `${words} words`}
          </Text>
          {settings.seriesGuide !== null && (
            <View testID={settingsTestIds.styleGuideSeries} style={styles.series}>
              <Text style={styles.seriesHead}>
                Series · {settings.seriesName} style guide, also sent
              </Text>
              <Text style={styles.seriesBody}>{settings.seriesGuide}</Text>
            </View>
          )}
          <Text testID={settingsTestIds.styleGuideNote} style={styles.muted}>
            Sent with every AI draft from this book, with the beat, its context and the scene so
            far. It doesn’t change text already written.
          </Text>
        </ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    fill: { flex: 1, backgroundColor: theme.colors.bg },
    content: { padding: theme.space[4], gap: theme.space[3] },
    muted: { fontSize: theme.text.sm, color: theme.colors.muted },
    series: {
      gap: theme.space[1],
      padding: theme.space[3],
      borderRadius: theme.radius.md,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    seriesHead: { fontSize: theme.text.xs, color: theme.colors.muted },
    seriesBody: { fontSize: theme.text.base, color: theme.colors.muted },
  });
}
