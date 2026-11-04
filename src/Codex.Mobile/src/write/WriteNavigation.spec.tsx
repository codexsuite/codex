import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import { writeLink } from '~/links';
import { writeTestIds } from './testIds';
import { openWrite } from './test-support/openWrite';

const pillLabel = () => screen.getByTestId(writeTestIds.pillLabel);

async function readyAt(url: string) {
  const opened = await openWrite({ url });
  await opened.page.ready();
  await opened.page.waitForLoads(1);
  return opened;
}

describe('Write: where it opens', () => {
  it('Write_WithASceneParam_ShouldOpenItsChapterScrolledToTheScene', async () => {
    const { page } = await readyAt('/book/book-1/write?sceneId=s4');

    expect(page.loads()[0]).toMatchObject({ scrollToSceneId: 's4' });
    expect(page.loads()[0]?.scenes.map((s) => s.id)).toEqual(['s4']);
    expect(pillLabel()).toHaveTextContent('3 · Grey Ink');
  });

  it('Write_WithAChapterParam_ShouldOpenThatChapterAtItsTop', async () => {
    const { page } = await readyAt('/book/book-1/write?chapterId=c2');

    expect(page.loads()[0]).toMatchObject({ scrollToSceneId: null });
    expect(page.loads()[0]?.scenes.map((s) => s.id)).toEqual(['s3']);
  });

  it('Write_WithNoParam_ShouldOpenTheFirstChapter', async () => {
    const { page } = await readyAt('/book/book-1/write');

    expect(page.loads()[0]?.scenes.map((s) => s.id)).toEqual(['s1', 's2']);
  });

  it('Write_WithAnUnknownScene_ShouldOpenTheFirstChapter', async () => {
    const { page } = await readyAt('/book/book-1/write?sceneId=gone');

    expect(page.loads()[0]?.scenes.map((s) => s.id)).toEqual(['s1', 's2']);
  });
});

describe('Write: the params change on the mounted screen', () => {
  it('OpenInWrite_ForASceneInAnotherChapter_ShouldLoadThatChapterOnTheSameScreen', async () => {
    const { page } = await readyAt('/book/book-1/write');

    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's3' })));
    await page.waitForLoads(2);

    expect(page.loads()[1]).toMatchObject({ scrollToSceneId: 's3' });
    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s3']);
    expect(pillLabel()).toHaveTextContent('2 · The Bell');
    expect(screen.getAllByTestId(writeTestIds.screen)).toHaveLength(1);
  });

  it('OpenInWrite_ForAnotherSceneInTheSameChapter_ShouldScrollWithoutReloading', async () => {
    const { page } = await readyAt('/book/book-1/write?sceneId=s1');

    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's2' })));

    await waitFor(() =>
      expect(page.sent().filter((m) => m.type === 'scrollTo')).toEqual([
        { type: 'scrollTo', sceneId: 's2' },
      ]),
    );
    expect(page.loads()).toHaveLength(1);
  });

  it('OpenInWrite_ForTheSameSceneTwice_ShouldNotReload', async () => {
    const { page } = await readyAt('/book/book-1/write?sceneId=s1');

    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's1' })));

    expect(page.loads()).toHaveLength(1);
  });
});

describe('Write: the chapter pill', () => {
  it('Next_ShouldLoadTheNextChapterAcrossActsAndKeepTheParamsInStep', async () => {
    const { app, page } = await readyAt('/book/book-1/write');

    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);
    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(3);

    expect(page.loads().map((l) => l.scenes[0]?.id)).toEqual(['s1', 's3', 's4']);
    expect(app.getSearchParams()).toMatchObject({ chapterId: 'c3' });
    expect(pillLabel()).toHaveTextContent('3 · Grey Ink');
  });

  it('Previous_AfterALinkToAScene_ShouldLeaveTheSceneBehind', async () => {
    const { app, page } = await readyAt('/book/book-1/write?sceneId=s3');

    await fireEvent.press(screen.getByTestId(writeTestIds.pillPrevious));
    await page.waitForLoads(2);

    expect(page.loads()[1]).toMatchObject({ scrollToSceneId: null });
    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s1', 's2']);
    expect(app.getSearchParams().sceneId).toBeUndefined();
  });

  it('PreviousAndNext_AtTheEndsOfTheBook_ShouldDoNothing', async () => {
    const { page } = await readyAt('/book/book-1/write');
    const user = userEvent.setup();

    await user.press(screen.getByTestId(writeTestIds.pillPrevious));
    expect(page.loads()).toHaveLength(1);

    await act(async () => router.navigate(writeLink('book-1', { chapterId: 'c3' })));
    await page.waitForLoads(2);
    await user.press(screen.getByTestId(writeTestIds.pillNext));
    expect(page.loads()).toHaveLength(2);
  });

  it('Pill_ShouldOpenTheChapterJumpSheetOnTheChapterBeingRead', async () => {
    const { app } = await readyAt('/book/book-1/write?chapterId=c2');

    await fireEvent.press(screen.getByTestId(writeTestIds.pill));

    expect(await screen.findByTestId(writeTestIds.chapterJumpSheet)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/chapters');
    expect(app.getSearchParams()).toMatchObject({ chapterId: 'c2' });
  });

  it('ChapterJump_PickingAChapter_ShouldCloseTheSheetAndLoadThatChapterEvenAfterASceneLink', async () => {
    const { app, page } = await readyAt('/book/book-1/write?sceneId=s1');
    await fireEvent.press(screen.getByTestId(writeTestIds.pill));
    await screen.findByTestId(writeTestIds.chapterJumpSheet);

    await fireEvent.press(await screen.findByTestId(writeTestIds.chapterJumpRow('c3')));

    await page.waitForLoads(2);
    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s4']);
    expect(page.loads()[1]).toMatchObject({ scrollToSceneId: null });
    expect(app.getPathname()).toBe('/book/book-1/write');
    expect(pillLabel()).toHaveTextContent('3 · Grey Ink');
  });
});

describe('Write: scene cards', () => {
  it('OpenScene_ShouldOpenTheSceneSheetOverWrite', async () => {
    const { app, page } = await readyAt('/book/book-1/write');

    await page.say({ type: 'openScene', sceneId: 's2' });

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/scene/s2'));
  });
});
