import { ApiRequestError } from '@codex/core/api/client';
import { getKbEntry, kbEntryKey, type KbEntry } from '@codex/core/api/kbEntries';
import { useQuery } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type ScrollViewInstance,
} from 'react-native';
import { useAdditions } from '~/book/queries';
import { useBookId } from '~/book/useBookId';
import { recordLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { EmptyState, ErrorState, PushBar, SectionHeader, SwitchRow } from '~/ui';
import { AdditionsSection } from './AdditionsSection';
import { AliasList } from './AliasList';
import { EntryDock } from './EntryDock';
import { EntryHead } from './EntryHead';
import { SummaryField } from './SummaryField';
import { UsedInSection } from './UsedInSection';
import { additionsFor, usesInBook } from './entryUses';
import { kbTestIds } from './testIds';
import { useCanWrite } from './useCanWrite';
import { useEntryPatch } from './useEntryPatch';
import { useSummaryAutosave } from './useSummaryAutosave';

/** How long Record waits for the summary to be saved before it says so. */
const RECORD_WAIT_MS = 10_000;

/** One Knowledge base entry, pushed on the Knowledge tab. */
export function EntryScreen() {
  const bookId = useBookId();
  const { entryId } = useLocalSearchParams<{ entryId: string }>();
  const { styles, theme } = useStyles(makeStyles);
  const entry = useQuery({
    queryKey: kbEntryKey(entryId),
    queryFn: ({ signal }) => getKbEntry(entryId, { signal }),
  });

  return (
    <View testID={kbTestIds.entryScreen} style={styles.screen}>
      <PushBar parent="Knowledge" title="" />
      {entry.isPending ? (
        <View testID={kbTestIds.entryLoading} style={styles.centered}>
          <ActivityIndicator color={theme.colors.accent} />
        </View>
      ) : entry.isError ? (
        entry.error instanceof ApiRequestError && entry.error.status === 404 ? (
          <EmptyState
            testID={kbTestIds.entryNotFound}
            title="Entry not found"
            message="It may have been deleted."
            action={{ label: 'Back to Knowledge', onPress: () => router.back() }}
          />
        ) : (
          <ErrorState
            testID={kbTestIds.entryError}
            error={entry.error}
            fallback="Couldn't load the entry."
            onRetry={() => void entry.refetch()}
          />
        )
      ) : (
        <EntryBody bookId={bookId} entry={entry.data} />
      )}
    </View>
  );
}

function EntryBody({ bookId, entry }: { bookId: string; entry: KbEntry }) {
  const { styles } = useStyles(makeStyles);
  const patch = useEntryPatch(bookId, entry.id);
  const { summary, setSummary, flush, store } = useSummaryAutosave(
    bookId,
    entry.id,
    entry.summary,
    (value) => patch({ summary: value }),
  );
  const canWrite = useCanWrite(bookId);
  // The switch follows the entry (a refetch changes it); while a change is being saved it shows the new position.
  const [pendingAlways, setPendingAlways] = useState<boolean | null>(null);
  const always = pendingAlways ?? entry.alwaysInContext;
  const [error, setError] = useState<string | null>(null);
  const recording = useRef(false);
  const [recordWaiting, setRecordWaiting] = useState(false);
  const [summaryNotSaved, setSummaryNotSaved] = useState(false);
  const scroll = useRef<ScrollViewInstance>(null);
  const usedInY = useRef(0);

  // Additions are the book's; a failed load leaves the section empty rather than blocking the entry.
  const additions = useAdditions(bookId);
  const uses = usesInBook(entry.usedIn, bookId);

  // The landing appends to the saved summary, so what is typed has to be on the server first: wait for it (at most
  // RECORD_WAIT_MS) and open the sheet only when it is. One press at a time.
  async function record() {
    if (recording.current) return;
    recording.current = true;
    setRecordWaiting(true);
    setSummaryNotSaved(false);
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        flush(),
        new Promise<void>((resolve) => {
          timer = setTimeout(resolve, RECORD_WAIT_MS);
        }),
      ]);
      if (store.getState() === 'saved') {
        router.push(recordLink(bookId, { kind: 'entry', id: entry.id }));
      } else {
        setSummaryNotSaved(true);
      }
    } finally {
      clearTimeout(timer);
      recording.current = false;
      setRecordWaiting(false);
    }
  }

  async function toggleAlways(value: boolean) {
    setPendingAlways(value);
    try {
      await patch({ alwaysInContext: value });
      setError(null);
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'That change could not be saved.');
    } finally {
      setPendingAlways(null);
    }
  }

  return (
    <>
      <ScrollView
        testID={kbTestIds.entryScroll}
        ref={scroll}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.content}
      >
        <EntryHead
          categoryName={entry.categoryName}
          colour={entry.categoryColour}
          name={entry.name}
        />
        <AliasList
          entryId={entry.id}
          name={entry.name}
          aliases={entry.aliases}
          readOnly={!canWrite}
          onChange={(aliases, allowDuplicate) =>
            patch({ aliases, ...(allowDuplicate ? { allowDuplicate } : {}) })
          }
        />
        <SummaryField
          value={summary}
          onChange={setSummary}
          onBlur={() => void flush()}
          editable={canWrite}
        />
        <SectionHeader title="Generation" />
        <SwitchRow
          testID={kbTestIds.always}
          label="Always in context"
          description="Sent with every AI draft"
          value={always}
          disabled={!canWrite}
          onValueChange={(value) => void toggleAlways(value)}
        />
        {error !== null && (
          <Text testID={kbTestIds.changeError} accessibilityRole="alert" style={styles.error}>
            {error}
          </Text>
        )}
        <UsedInSection
          bookId={bookId}
          uses={uses}
          onLayoutY={(y) => {
            usedInY.current = y;
          }}
        />
        <AdditionsSection
          colour={entry.categoryColour}
          additions={additionsFor(additions.data?.additions ?? [], entry.id, entry.usedIn)}
        />
      </ScrollView>
      {summaryNotSaved && (
        <Text testID={kbTestIds.summaryNotSaved} accessibilityRole="alert" style={styles.error}>
          Summary not saved yet. Try Record again when it has saved.
        </Text>
      )}
      <EntryDock
        onScenes={() => scroll.current?.scrollTo({ y: usedInY.current, animated: true })}
        canRecord={canWrite && !recordWaiting}
        onRecord={() => void record()}
      />
    </>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.bg },
    centered: { flex: 1, alignItems: 'center', justifyContent: 'center' },
    content: { paddingBottom: theme.space[5] },
    error: {
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[2],
      fontSize: theme.text.sm,
      color: theme.colors.danger,
    },
  });
}
