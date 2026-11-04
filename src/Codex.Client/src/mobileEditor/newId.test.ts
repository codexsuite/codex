import { describe, expect, it } from 'vitest';
import { newId } from './newId';

describe('newId', () => {
  it('newId_WhenCalled_ShouldBeAVersion4Uuid', () => {
    expect(newId()).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
    );
  });

  it('newId_WhenCalledTwice_ShouldDiffer', () => {
    expect(newId()).not.toBe(newId());
  });

  it('newId_WhenGivenBytes_ShouldSetVersionAndVariantBits', () => {
    expect(newId((bytes) => bytes.fill(0xff))).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff');
  });
});
