import { describe, expect, it } from 'vitest';
import createdDeviceSession from '../../../Codex.TestSupport/Fixtures/created-device-session.json';
import type { CreatedDeviceSession } from './auth';

// The same JSON file the C# DtoFixtureTests round-trip as CreatedDeviceSessionDto. `satisfies` makes tsc fail if the
// type drifts from it.
describe('device session contract', () => {
  it('CreatedDeviceSessionDto round-trips', () => {
    const typed = createdDeviceSession satisfies CreatedDeviceSession;
    expect(JSON.parse(JSON.stringify(typed))).toEqual(createdDeviceSession);
  });

  it('carries a secret in the cdx_dev_ format', () => {
    expect(createdDeviceSession.secret).toMatch(/^cdx_dev_[0-9A-Za-z]{43}$/);
    expect(createdDeviceSession.session.prefix).toBe(createdDeviceSession.secret.slice(0, 16));
  });
});
