import type { KbUse } from '@codex/core/api/kbEntries';
import { router } from 'expo-router';
import { View } from 'react-native';
import { sceneSheetLink } from '~/links';
import { Row, SectionHeader } from '~/ui';
import { useLabel } from './entryUses';
import { kbTestIds } from './testIds';

/** The scenes of this book that use the entry; tapping one opens its sheet. */
export function UsedInSection({
  bookId,
  uses,
  onLayoutY,
}: {
  bookId: string;
  uses: KbUse[];
  /** Where the section starts, for the dock's Scenes button. */
  onLayoutY: (y: number) => void;
}) {
  return (
    <View testID={kbTestIds.usedIn} onLayout={(e) => onLayoutY(e.nativeEvent.layout.y)}>
      <SectionHeader title="Used in" count={uses.length} />
      {uses.map((use) => (
        <Row
          key={use.sceneId}
          testID={kbTestIds.usedInRow(use.sceneId)}
          label={useLabel(use)}
          detail={use.snippet === '' ? undefined : use.snippet}
          onPress={() => router.push(sceneSheetLink(bookId, use.sceneId))}
        />
      ))}
    </View>
  );
}
