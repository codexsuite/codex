// A stand-in for expo-audio in specs, registered for every spec in `jestSetup.ts` (reset before each test).
// The microphone is `audioHarness`: the spec decides whether the permission is granted and how much audio "arrives".

interface Buffer {
  data: ArrayBuffer;
  sampleRate: number;
  channels: number;
  timestamp: number;
}

let onBuffer: ((buffer: Buffer) => void) | undefined;
let segmentCount = 0;

export const audioHarness = {
  granted: true,
  /** Makes `stream.start()` reject with this message. */
  startFailure: null as string | null,
  /** Makes `startFileRecordingAsync` reject (after the stream started). */
  fileStartFailure: null as string | null,
  /** Makes `stopFileRecordingAsync` reject. */
  fileStopFailure: null as string | null,
  /** Reports finished files the way Android does: `file:/cache/...`. */
  oneSlashUris: false,
  lastRequestedUri: null as string | null,
  startedSegments: 0,
  stoppedSegments: 0,
  streamStops: 0,
  recording: false,
  /** Delivers `seconds` of 16 kHz mono int16 audio to the hook's `onBuffer`. */
  emitSeconds(seconds: number) {
    onBuffer?.({
      data: new ArrayBuffer(32_000 * seconds),
      sampleRate: 16_000,
      channels: 1,
      timestamp: 0,
    });
  },
  reset() {
    this.granted = true;
    this.startFailure = null;
    this.fileStartFailure = null;
    this.fileStopFailure = null;
    this.oneSlashUris = false;
    this.lastRequestedUri = null;
    this.startedSegments = 0;
    this.stoppedSegments = 0;
    this.streamStops = 0;
    this.recording = false;
    segmentCount = 0;
    onBuffer = undefined;
  },
};

const stream = {
  start: () =>
    audioHarness.startFailure === null
      ? Promise.resolve()
      : Promise.reject(new Error(audioHarness.startFailure)),
  stop: () => void (audioHarness.streamStops += 1),
  startFileRecordingAsync: (options?: { uri?: string }) => {
    if (audioHarness.fileStartFailure !== null) {
      return Promise.reject(new Error(audioHarness.fileStartFailure));
    }
    audioHarness.lastRequestedUri = options?.uri ?? null;
    audioHarness.startedSegments += 1;
    audioHarness.recording = true;
    return Promise.resolve({ uri: 'file:///cache/pending.wav' });
  },
  stopFileRecordingAsync: () => {
    audioHarness.stoppedSegments += 1;
    audioHarness.recording = false;
    segmentCount += 1;
    if (audioHarness.fileStopFailure !== null) {
      return Promise.reject(new Error(audioHarness.fileStopFailure));
    }
    const uri = audioHarness.lastRequestedUri ?? `file:///cache/recording-${segmentCount}.wav`;
    return Promise.resolve({
      uri: audioHarness.oneSlashUris ? uri.replace('file:///', 'file:/') : uri,
      duration: 1,
      size: 32_044,
      sampleRate: 16_000,
      channels: 1,
      encoding: 'int16' as const,
    });
  },
};

export function useAudioStream(options: { onBuffer?: (buffer: Buffer) => void } = {}) {
  onBuffer = options.onBuffer;
  return { stream, isStreaming: audioHarness.recording };
}

export const requestRecordingPermissionsAsync = () =>
  Promise.resolve({
    granted: audioHarness.granted,
    status: audioHarness.granted ? 'granted' : 'denied',
  });
