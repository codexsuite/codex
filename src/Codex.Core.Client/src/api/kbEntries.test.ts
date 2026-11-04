import { describe, expect, it } from 'vitest';
import entry from '../../../Codex.TestSupport/Fixtures/kb/entry.json';
import entryRequests from '../../../Codex.TestSupport/Fixtures/kb/entry-requests.json';
import lifecycle from '../../../Codex.TestSupport/Fixtures/kb/entry-lifecycle.json';
import type {
  AddKbRelatedRequest,
  KbDeleteImpact,
  KbUndoRecord,
  MakeKbMentionRequest,
  CreateKbCategoryRequest,
  CreateKbEntryRequest,
  KbEntry,
  MoveKbEntryRequest,
  PatchKbEntryRequest,
} from './kbEntries';

// The same JSON files the C# KbEntryFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('knowledge base entry contract fixtures', () => {
  it('KbEntryDto round-trips', () => {
    const typed = entry satisfies KbEntry;
    expect(roundTrip(typed)).toEqual(entry);
  });

  it('entry requests round-trip', () => {
    const create = entryRequests.create satisfies CreateKbEntryRequest;
    const patch = entryRequests.patch satisfies PatchKbEntryRequest;
    const move = entryRequests.move satisfies MoveKbEntryRequest;
    const category = entryRequests.category satisfies CreateKbCategoryRequest;
    expect(roundTrip([create, patch, move, category])).toEqual([
      entryRequests.create,
      entryRequests.patch,
      entryRequests.move,
      entryRequests.category,
    ]);
  });

  it('lifecycle DTOs round-trip', () => {
    const typed = [
      lifecycle.addRelated satisfies AddKbRelatedRequest,
      lifecycle.makeMention satisfies MakeKbMentionRequest,
      lifecycle.impact satisfies KbDeleteImpact,
      lifecycle.undo satisfies KbUndoRecord,
    ];
    expect(roundTrip(typed)).toEqual([
      lifecycle.addRelated,
      lifecycle.makeMention,
      lifecycle.impact,
      lifecycle.undo,
    ]);
  });
});
