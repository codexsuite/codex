import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useKb } from '~/book/queries';
import type { RecordTarget } from '~/links';
import { SpeechEngineError, useActiveSpeechModel, useSpeechEngine } from '~/speech';
import { Button, Sheet } from '~/ui';
import type { Theme } from '../theme';
import { useStyles } from '../theme/useStyles';
import { defaultLandingMode } from './LandingChoices';
import { landTranscript, type Landing, type LandingProgress } from './landTranscript';
import { NoSpeechModel } from './NoSpeechModel';
import { RecordCapture } from './RecordCapture';
import { recordTestIds } from './testIds';
import { transcribeSegments } from './transcribeSegments';
import { TranscriptReview, type ReviewDraft } from './TranscriptReview';
import { useRecordTarget } from './useRecordTarget';
import { useWavRecorder } from './useWavRecorder';

type Phase = 'capture' | 'transcribing' | 'transcribe-failed' | 'review';

const messageOf = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

/** The Record sheet for one target: capture, transcribe on the phone, then land the text on the Knowledge base. */
export function RecordSheet({ bookId, target }: { bookId: string; target: RecordTarget }) {
  const { styles } = useStyles(makeStyles);
  const engine = useSpeechEngine();
  const queryClient = useQueryClient();
  const recorder = useWavRecorder();
  const model = useActiveSpeechModel();
  const { label } = useRecordTarget(bookId, target);
  const kb = useKb(bookId);

  const [phase, setPhase] = useState<Phase>('capture');
  const [files, setFiles] = useState<string[]>([]);
  const [transcribeError, setTranscribeError] = useState<string | null>(null);
  const [draft, setDraft] = useState<ReviewDraft>({
    transcript: '',
    mode: defaultLandingMode(target),
    entryName: '',
    categoryId: null,
    pickedEntryId: null,
  });
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const abort = useRef<AbortController | null>(null);
  const progress = useRef<LandingProgress>({});
  const progressFor = useRef<string | null>(null);

  useEffect(() => () => abort.current?.abort(), []);

  const transcribe = async (segments: readonly string[]) => {
    setPhase('transcribing');
    setTranscribeError(null);
    const controller = new AbortController();
    abort.current = controller;
    try {
      const transcript = await transcribeSegments(engine, segments, controller.signal);
      setDraft((current) => ({ ...current, transcript }));
      setPhase('review');
    } catch (error) {
      if (error instanceof SpeechEngineError && error.code === 'cancelled') return;
      setTranscribeError(messageOf(error, 'The recording could not be transcribed.'));
      setPhase('transcribe-failed');
    }
  };

  const stop = async () => {
    const recorded = await recorder.stop();
    if (recorded === null) return;
    setFiles(recorded);
    await transcribe(recorded);
  };

  const close = async () => {
    abort.current?.abort();
    await recorder.discard();
    router.back();
  };

  const categoryId = draft.categoryId ?? kb.data?.categories[0]?.id ?? null;
  const landing: Landing | null =
    draft.mode === 'new-entry' &&
    target.kind === 'scene' &&
    categoryId !== null &&
    draft.entryName.trim() !== ''
      ? { kind: 'new-entry', sceneId: target.id, name: draft.entryName.trim(), categoryId }
      : draft.mode === 'add-to-an-entry' && target.kind === 'scene' && draft.pickedEntryId !== null
        ? { kind: 'add-to-entry', entryId: draft.pickedEntryId, sceneId: target.id }
        : draft.mode === 'add-to-entry' && target.kind === 'entry'
          ? { kind: 'add-to-entry', entryId: target.id, sceneId: null }
          : null;
  const canSave = landing !== null && draft.transcript.trim() !== '' && !saving;

  const save = async () => {
    if (landing === null) return;
    // A retry continues a half-finished save only if it is the same save: another entry, name, category or text starts over.
    const saveKey = JSON.stringify([landing, draft.transcript.trim()]);
    if (progressFor.current !== saveKey) {
      progress.current = {};
      progressFor.current = saveKey;
    }
    setSaving(true);
    setSaveError(null);
    try {
      await landTranscript(queryClient, bookId, landing, draft.transcript.trim(), progress.current);
      await recorder.discard();
      router.back();
    } catch (error) {
      setSaveError(messageOf(error, 'The text could not be saved. Try again.'));
      setSaving(false);
    }
  };

  const noModel =
    phase === 'capture' && recorder.status === 'idle' && !model.loading && model.activeId === null;

  return (
    <Sheet
      title="Record"
      onClose={() => void close()}
      testID={recordTestIds.sheet}
      dock={
        phase === 'review'
          ? {
              primary: {
                label: 'Save',
                onPress: () => void save(),
                disabled: !canSave,
                busy: saving,
                testID: recordTestIds.save,
              },
              secondary: {
                label: 'Discard',
                onPress: () => void close(),
                testID: recordTestIds.discard,
              },
            }
          : undefined
      }
    >
      <ScrollView keyboardShouldPersistTaps="handled">
        <Text testID={recordTestIds.target} style={styles.target}>
          {label ?? 'Recording'}
        </Text>
        {noModel && <NoSpeechModel />}
        {phase === 'capture' && !noModel && (
          <RecordCapture
            recorder={recorder}
            onStop={() => void stop()}
            onDiscard={() => void close()}
          />
        )}
        {phase === 'transcribing' && (
          <View testID={recordTestIds.transcribing} style={styles.busy}>
            <ActivityIndicator />
            <Text style={styles.busyText}>Transcribing on this phone…</Text>
          </View>
        )}
        {phase === 'transcribe-failed' && (
          <View style={styles.busy}>
            <Text
              testID={recordTestIds.transcribeError}
              accessibilityRole="alert"
              style={styles.error}
            >
              {transcribeError}
            </Text>
            <Button
              label="Try again"
              variant="primary"
              onPress={() => void transcribe(files)}
              testID={recordTestIds.retry}
            />
            <Button label="Discard" onPress={() => void close()} testID={recordTestIds.discard} />
          </View>
        )}
        {phase === 'review' && (
          <TranscriptReview
            target={target}
            kb={kb}
            draft={draft}
            onChange={(patch) => setDraft((current) => ({ ...current, ...patch }))}
            saveError={saveError}
          />
        )}
      </ScrollView>
    </Sheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    target: {
      paddingHorizontal: theme.space[4],
      fontSize: theme.text.sm,
      color: theme.colors.muted,
    },
    busy: { alignItems: 'center', gap: theme.space[3], padding: theme.space[5] },
    busyText: { fontSize: theme.text.base, color: theme.colors.muted },
    error: { fontSize: theme.text.base, color: theme.colors.danger, textAlign: 'center' },
  });
}
