import { ScrollView, StyleSheet } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Chip } from '~/ui';
import { sameFilter, type KbFilter, type KbStripChip } from './kbList';
import { kbTestIds } from './testIds';

/** The horizontal strip of filters under the search: All, each category with its count, Unused. */
export function CategoryStrip({
  chips,
  filter,
  onFilter,
}: {
  chips: KbStripChip[];
  filter: KbFilter;
  onFilter: (filter: KbFilter) => void;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <ScrollView
      testID={kbTestIds.strip}
      horizontal
      showsHorizontalScrollIndicator={false}
      style={styles.strip}
      contentContainerStyle={styles.content}
    >
      {chips.map((chip) => (
        <Chip
          key={chip.key}
          testID={kbTestIds.chip(chip.key)}
          label={chip.label}
          count={chip.count}
          selected={sameFilter(filter, chip.filter)}
          onPress={() => onFilter(chip.filter)}
        />
      ))}
    </ScrollView>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    strip: { flexGrow: 0 },
    content: {
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingVertical: theme.space[2],
    },
  });
}
