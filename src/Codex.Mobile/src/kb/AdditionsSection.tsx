import { StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { SectionHeader } from '~/ui';
import { categoryColour } from './categoryColour';
import type { EntryAddition } from './entryUses';
import { kbTestIds } from './testIds';

/** What the book has added to the entry, scene by scene (read-only on the phone). */
export function AdditionsSection({
  additions,
  colour,
}: {
  additions: EntryAddition[];
  /** The entry's category colour token: the card's left border. */
  colour: string | null;
}) {
  const { theme, styles } = useStyles(makeStyles);
  return (
    <View testID={kbTestIds.additions}>
      <SectionHeader title="Additions" count={additions.length} />
      {additions.map((a) => (
        <View
          key={a.id}
          testID={kbTestIds.addition(a.id)}
          style={[styles.card, { borderLeftColor: categoryColour(theme, colour) }]}
        >
          {a.where !== null && <Text style={styles.where}>{a.where}</Text>}
          <Text style={styles.body}>{a.body}</Text>
        </View>
      ))}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    card: {
      gap: 2,
      marginHorizontal: theme.space[4],
      marginTop: theme.space[2],
      padding: theme.space[3],
      borderRadius: theme.radius.md,
      backgroundColor: theme.colors.surface,
      borderLeftWidth: 3,
    },
    where: { fontSize: theme.text.xs, color: theme.colors.muted },
    body: { fontSize: theme.text.base, color: theme.colors.text },
  });
}
