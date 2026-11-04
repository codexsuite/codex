import { useLocalSearchParams } from 'expo-router';
import { parseRecordTarget } from '~/links';
import { RecordSheet } from '~/recording/RecordSheet';
import { EmptyState, Sheet } from '~/ui';

export default function Record() {
  const { bookId, target } = useLocalSearchParams<{ bookId: string; target: string }>();
  const parsed = parseRecordTarget(target);
  if (parsed === null) {
    return (
      <Sheet title="Record">
        <EmptyState
          title="Nothing to record to"
          message="Start a recording from a scene or an entry."
        />
      </Sheet>
    );
  }
  return <RecordSheet bookId={bookId} target={parsed} />;
}
