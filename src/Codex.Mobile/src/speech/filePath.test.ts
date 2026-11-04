import { describe, expect, it } from 'vitest';
import { filePathOf, normaliseFileUri } from './filePath';

describe('normaliseFileUri', () => {
  it.each(['file:/data/a.wav', 'file://data/a.wav', 'file:///data/a.wav', 'file:////data/a.wav'])(
    '%s_ShouldBecomeThreeSlashes',
    (uri) => {
      expect(normaliseFileUri(uri)).toBe('file:///data/a.wav');
    },
  );
});

describe('filePathOf', () => {
  it.each(['file:/data/a.wav', 'file://data/a.wav', 'file:///data/a.wav'])(
    '%s_ShouldBeThePlainPath',
    (uri) => {
      expect(filePathOf(uri)).toBe('/data/a.wav');
    },
  );
  it('ShouldDecodeEscapes', () => {
    expect(filePathOf('file:///data/my%20files/a.wav')).toBe('/data/my files/a.wav');
  });
  it('WithAMalformedEscape_ShouldKeepTheRawPath', () => {
    expect(filePathOf('file:///data/100%.wav')).toBe('/data/100%.wav');
  });
  it('WithAPlainPath_ShouldPassThrough', () => {
    expect(filePathOf('/data/a.wav')).toBe('/data/a.wav');
  });
});
