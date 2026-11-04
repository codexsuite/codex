import { describe, expect, it } from 'vitest';
import { appendToSummary, bufferSeconds, formatTimer, joinTranscripts } from './segments';

describe('formatTimer', () => {
  it.each([
    [0, '00:00'],
    [74.9, '01:14'],
    [3599, '59:59'],
    [3723, '1:02:03'],
    [-5, '00:00'],
  ])('Seconds_%d_ShouldBe_%s', (seconds, text) => {
    expect(formatTimer(seconds)).toBe(text);
  });
});

describe('bufferSeconds', () => {
  it('SixteenKilohertzMonoInt16_OneSecondIs32000Bytes', () => {
    expect(bufferSeconds(32_000, { sampleRate: 16_000, channels: 1, bytesPerSample: 2 })).toBe(1);
  });
  it('Stereo_HalvesTheFrames', () => {
    expect(bufferSeconds(32_000, { sampleRate: 16_000, channels: 2, bytesPerSample: 2 })).toBe(0.5);
  });
  it('WithNoSampleRate_ShouldBeZero', () => {
    expect(bufferSeconds(100, { sampleRate: 0, channels: 1, bytesPerSample: 2 })).toBe(0);
  });
});

describe('joinTranscripts', () => {
  it('ShouldTrimDropEmptyOnesAndJoinWithASpace', () => {
    expect(joinTranscripts([' One. ', '', '  ', 'Two.'])).toBe('One. Two.');
  });
});

describe('appendToSummary', () => {
  it('WithASummary_ShouldAddAParagraph', () => {
    expect(appendToSummary('The queen.\n', ' She lies. ')).toBe('The queen.\n\nShe lies.');
  });
  it('WithAnEmptySummary_ShouldBeTheTranscript', () => {
    expect(appendToSummary('', 'She lies.')).toBe('She lies.');
  });
});
