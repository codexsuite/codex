import { describe, expect, it } from 'vitest';
import jobs from '../../../Codex.TestSupport/Fixtures/jobs/jobs.json';
import kb from '../../../Codex.TestSupport/Fixtures/kb/kb.json';
import linkRequests from '../../../Codex.TestSupport/Fixtures/kb/link-requests.json';
import clearanceSummary from '../../../Codex.TestSupport/Fixtures/references/clearance-summary.json';
import markers from '../../../Codex.TestSupport/Fixtures/references/markers.json';
import type { Job } from './jobs';
import type { Kb, LinkKbEntryRequest, SetBeatContextRequest } from './kb';
import type { ClearanceSummary, Marker } from './references';

// The same JSON files the C# ReadWriteDtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('knowledge base, references and job contract fixtures', () => {
  it('KbDto round-trips', () => {
    const typed = kb satisfies Kb;
    expect(roundTrip(typed)).toEqual(kb);
  });

  it('KB link requests round-trip', () => {
    const link = linkRequests.link satisfies LinkKbEntryRequest;
    const context = linkRequests.beatContext satisfies SetBeatContextRequest;
    expect(roundTrip(link)).toEqual(linkRequests.link);
    expect(roundTrip(context)).toEqual(linkRequests.beatContext);
  });

  it('MarkerDto list round-trips', () => {
    const typed: Marker[] = markers;
    expect(roundTrip(typed)).toEqual(markers);
  });

  it('ClearanceSummaryDto round-trips', () => {
    const typed = clearanceSummary satisfies ClearanceSummary;
    expect(roundTrip(typed)).toEqual(clearanceSummary);
  });

  it('JobDto list round-trips', () => {
    const typed: Job[] = jobs;
    expect(roundTrip(typed)).toEqual(jobs);
  });
});
