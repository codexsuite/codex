import { StyleSheet, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { TextField } from '~/ui';
import { kbTestIds } from './testIds';

/** The entry's summary: edits save by themselves (the owner passes the autosave). */
export function SummaryField({
  value,
  onChange,
  onBlur,
  editable,
}: {
  value: string;
  onChange: (summary: string) => void;
  onBlur: () => void;
  editable: boolean;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.box}>
      <TextField
        testID={kbTestIds.summary}
        label="Summary"
        placeholder="What the book needs to remember"
        value={value}
        onChangeText={onChange}
        onBlur={onBlur}
        editable={editable}
        multiline
        minLines={5}
      />
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { paddingHorizontal: theme.space[4], paddingTop: theme.space[4] },
  });
}
