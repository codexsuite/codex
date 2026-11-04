import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import outline from '../../../Codex.TestSupport/Fixtures/structure/outline.json';
import { archivedScenesKey } from '@codex/core/api/plan';
import { invalidateOutline, outlineKey, updateOutlineScene } from '@codex/core/api/outline';
import type { Outline } from '@codex/core/api/structure';
import { setSceneSummary } from '@codex/core/api/write';

const seeded = outline as Outline;
const bookId = seeded.bookId;
const firstScene = seeded.acts.flatMap((a) => a.chapters.flatMap((c) => c.scenes))[0];

const sceneIn = (client: QueryClient, id: string) =>
  client
    .getQueryData<Outline>(outlineKey(bookId))
    ?.acts.flatMap((a) => a.chapters.flatMap((c) => c.scenes))
    .find((s) => s.id === id);

// Plan and Write read one cached outline per book, so a save on either screen is what the other one shows.
describe('shared outline cache', () => {
  it('Summary_WhenSavedOnWrite_ShouldBeInTheOutlinePlanReads', () => {
    const client = new QueryClient();
    client.setQueryData(outlineKey(bookId), seeded);

    setSceneSummary(client, bookId, firstScene.id, 'Saved on Write.');

    expect(sceneIn(client, firstScene.id)?.summary).toBe('Saved on Write.');
  });

  it('Scene_WhenUpdated_ShouldLeaveOtherScenesAndAnUncachedOutlineAlone', () => {
    const client = new QueryClient();
    updateOutlineScene(client, bookId, firstScene.id, (s) => ({ ...s, title: 'x' }));
    expect(client.getQueryData(outlineKey(bookId))).toBeUndefined();

    client.setQueryData(outlineKey(bookId), seeded);
    updateOutlineScene(client, bookId, firstScene.id, (s) => ({ ...s, title: 'Renamed' }));
    const others = seeded.acts.flatMap((a) => a.chapters.flatMap((c) => c.scenes)).slice(1);
    expect(sceneIn(client, firstScene.id)?.title).toBe('Renamed');
    for (const s of others) expect(sceneIn(client, s.id)).toEqual(s);
  });

  it('Invalidate_ShouldAlsoMarkTheArchivedSceneListsStale', async () => {
    const client = new QueryClient();
    client.setQueryData(outlineKey(bookId), seeded);
    client.setQueryData(archivedScenesKey(bookId, 'chapter'), []);

    await invalidateOutline(client, bookId);

    expect(client.getQueryState(outlineKey(bookId))?.isInvalidated).toBe(true);
    expect(client.getQueryState(archivedScenesKey(bookId, 'chapter'))?.isInvalidated).toBe(true);
  });
});
