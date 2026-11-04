/** `74` → `01:14`; an hour or more is `1:02:03`. Whole seconds, rounded down. */
export function formatTimer(totalSeconds: number): string {
  const whole = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const seconds = whole % 60;
  const two = (n: number) => String(n).padStart(2, '0');
  return hours > 0 ? `${hours}:${two(minutes)}:${two(seconds)}` : `${two(minutes)}:${two(seconds)}`;
}

/** Seconds of audio in a PCM buffer: bytes / bytes per sample / channels / sample rate. */
export function bufferSeconds(
  byteLength: number,
  {
    sampleRate,
    channels,
    bytesPerSample,
  }: { sampleRate: number; channels: number; bytesPerSample: number },
): number {
  const frames = byteLength / (bytesPerSample * channels);
  return sampleRate > 0 ? frames / sampleRate : 0;
}

/** The segments' transcripts as one text: trimmed, empty ones dropped, a space between. */
export const joinTranscripts = (texts: readonly string[]): string =>
  texts
    .map((text) => text.trim())
    .filter((text) => text !== '')
    .join(' ');

/** The summary with a transcript added as a new paragraph (just the transcript when the summary is empty). */
export function appendToSummary(summary: string, transcript: string): string {
  const existing = summary.trimEnd();
  const added = transcript.trim();
  return existing === '' ? added : `${existing}\n\n${added}`;
}
