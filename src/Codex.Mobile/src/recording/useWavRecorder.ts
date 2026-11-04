import { requestRecordingPermissionsAsync, useAudioStream } from 'expo-audio';
import { useCallback, useEffect, useRef, useState } from 'react';
import { deleteAudioFiles, segmentUri, sweepStaleRecordings } from './audioFiles';
import { bufferSeconds } from './segments';

export type RecorderStatus =
  | 'idle'
  | 'recording'
  | 'paused'
  /** Stopped with its segments kept for transcription. */
  | 'stopped'
  | 'denied'
  | 'failed';

export interface WavRecorder {
  status: RecorderStatus;
  /** Seconds recorded so far, across pauses. */
  seconds: number;
  error: string | null;
  start(): Promise<void>;
  pause(): Promise<void>;
  resume(): Promise<void>;
  /** Ends the recording and returns the WAV files (one per pause-separated segment), or null when it failed. */
  stop(): Promise<string[] | null>;
  /** Ends the recording and deletes its files. */
  discard(): Promise<void>;
}

const bytesPerSample = 2; // encoding: 'int16'

/**
 * Records 16 kHz mono 16-bit WAV, the format whisper.cpp wants, with expo-audio's `useAudioStream` (the same file on both
 * platforms). The stream and the file writer have no pause, so Pause ends a segment and Resume starts the next one; the
 * caller transcribes the segments in order. The files are deleted when the component using this unmounts.
 */
export function useWavRecorder(): WavRecorder {
  const [status, setStatus] = useState<RecorderStatus>('idle');
  const [seconds, setSeconds] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const segments = useRef<string[]>([]);
  const capturing = useRef(false);
  const total = useRef(0);
  const started = useRef(0);
  const current = useRef<string | null>(null);

  const { stream } = useAudioStream({
    sampleRate: 16_000,
    channels: 1,
    encoding: 'int16',
    onBuffer(buffer) {
      if (!capturing.current) return;
      total.current += bufferSeconds(buffer.data.byteLength, {
        sampleRate: buffer.sampleRate,
        channels: buffer.channels,
        bytesPerSample,
      });
      setSeconds((shown) =>
        Math.floor(total.current) === Math.floor(shown) ? shown : total.current,
      );
    },
  });

  const fail = useCallback((cause: unknown, fallback: string) => {
    capturing.current = false;
    setError(cause instanceof Error ? cause.message : fallback);
    setStatus('failed');
  }, []);

  const beginSegment = useCallback(async () => {
    await stream.start();
    try {
      const uri = segmentUri(started.current);
      await stream.startFileRecordingAsync({ uri, format: 'wav' });
      started.current += 1;
      current.current = uri;
    } catch (cause) {
      stream.stop(); // the microphone must not stay open when no file is being written
      throw cause;
    }
    capturing.current = true;
  }, [stream]);

  const endSegment = useCallback(async () => {
    capturing.current = false;
    const expected = current.current;
    current.current = null;
    try {
      const { uri } = await stream.stopFileRecordingAsync();
      segments.current.push(uri);
    } catch (cause) {
      // The file we asked for may still hold audio; keep it so it can be deleted (or transcribed).
      if (expected !== null) segments.current.push(expected);
      throw cause;
    } finally {
      stream.stop();
    }
  }, [stream]);

  useEffect(
    () => () => {
      capturing.current = false;
      try {
        stream.stop();
      } catch {
        // Already stopped.
      }
      deleteAudioFiles(segments.current);
    },
    [stream],
  );

  const start = useCallback(async () => {
    setError(null);
    try {
      sweepStaleRecordings();
      const { granted } = await requestRecordingPermissionsAsync();
      if (!granted) {
        setStatus('denied');
        return;
      }
      await beginSegment();
      setStatus('recording');
    } catch (cause) {
      fail(cause, 'The microphone could not be started.');
    }
  }, [beginSegment, fail]);

  const pause = useCallback(async () => {
    try {
      await endSegment();
      setStatus('paused');
    } catch (cause) {
      fail(cause, 'The recording could not be paused.');
    }
  }, [endSegment, fail]);

  const resume = useCallback(async () => {
    try {
      await beginSegment();
      setStatus('recording');
    } catch (cause) {
      fail(cause, 'The recording could not be resumed.');
    }
  }, [beginSegment, fail]);

  const stop = useCallback(async () => {
    try {
      if (capturing.current) await endSegment();
      setStatus('stopped');
      return [...segments.current];
    } catch (cause) {
      fail(cause, 'The recording could not be saved.');
      return null;
    }
  }, [endSegment, fail]);

  const discard = useCallback(async () => {
    if (capturing.current) await endSegment().catch(() => undefined);
    deleteAudioFiles(segments.current);
    segments.current = [];
    total.current = 0;
    started.current = 0;
    setSeconds(0);
    setError(null);
    setStatus('idle');
  }, [endSegment]);

  return { status, seconds, error, start, pause, resume, stop, discard };
}
