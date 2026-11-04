import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { FAKE_BOOK_ID } from '~/test-support/fakes';
import { shellTestIds } from '~/ui';
import { writeTestIds } from './testIds';
import { openWrite } from './test-support/openWrite';
import { beat, writeOutline } from './test-support/world';

const conflict = { status: 409, body: { error: 'That scene changed.', code: 'conflict' } };
const saveDot = () => screen.getByTestId(shellTestIds.saveDot).props.accessibilityLabel as string;

async function readyChapterOne(options: Parameters<typeof openWrite>[0] = {}) {
  const opened = await openWrite(options);
  await opened.page.ready();
  await opened.page.waitForLoads(1);
  return opened;
}

describe('Write: saving', () => {
  it('Change_ShouldShowUnsavedAndNotCallTheServerBeforeTheDelay', async () => {
    const { app, page } = await readyChapterOne();

    await page.say({ type: 'change', sceneId: 's1', markdown: 'First, edited.\n' });

    expect(saveDot()).toBe('Save status: unsaved');
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(0);
  });

  it('Blur_ShouldSaveTheSceneWithTheLoadedVersionAndNeverSendSetSceneBack', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'focus', sceneId: 's1' });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'First, edited.\n' });

    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(saveDot()).toBe('Save status: saved'));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose').map((c) => c.body)).toEqual([
      { body: 'First, edited.\n', version: 1 },
    ]);
    // The page's text is the truth after the app's own save: typing during the save must not be wiped.
    expect(page.sent().filter((m) => m.type === 'setScene')).toEqual([]);
  });

  it('Change_WhenTheTextIsUnchanged_ShouldNotSave', async () => {
    const { app, page } = await readyChapterOne();

    await page.say({ type: 'change', sceneId: 's1', markdown: 'First.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });

    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(0);
    expect(saveDot()).toBe('Save status: saved');
  });

  it('Save_ShouldKeepEachScenesOwnVersion', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'One.\n' });
    await page.say({ type: 'change', sceneId: 's2', markdown: 'Two.\n' });

    await page.say({ type: 'blur', sceneId: 's2' });

    await waitFor(() => expect(saveDot()).toBe('Save status: saved'));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')[0]?.body).toEqual({
      body: 'One.\n',
      version: 1,
    });
    expect(app.api.callsTo('PUT /api/scenes/s2/prose')[0]?.body).toEqual({
      body: 'Two.\n',
      version: 2,
    });
  });

  it('LeavingTheTab_WithAnUnsavedEdit_ShouldSaveIt', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Left behind.\n' });

    await fireEvent.press(screen.getByTestId(shellTestIds.tab('knowledge')));

    await waitFor(() => expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')[0]?.body).toEqual({
      body: 'Left behind.\n',
      version: 1,
    });
  });

  it('ChapterChange_WithAnUnsavedEdit_ShouldSaveBeforeLoadingTheNextChapter', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's2', markdown: 'Second, edited.\n' });

    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);

    expect(app.api.callsTo('PUT /api/scenes/s2/prose')).toHaveLength(1);
    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s3']);
  });

  it('ChapterChange_BackToASavedChapter_ShouldLoadWhatWasSaved', async () => {
    const { page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Saved text.\n' });
    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);

    await fireEvent.press(screen.getByTestId(writeTestIds.pillPrevious));
    await page.waitForLoads(3);

    expect(page.loads()[2]?.scenes[0]).toMatchObject({
      id: 's1',
      prose: 'Saved text.\n',
      version: 2,
    });
  });

  // The prose PUT is last-write-wins (a stale version answers 200 with a bumped one), so the server does not send 409
  // today: this is the defensive path of the core controller.
  it('Save_IfAConflictIsRefusedAnyway_ShouldReadTheSceneAgainAndGiveThePageTheServersCopy', async () => {
    const { app, page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': conflict,
        'GET /api/scenes/s1/prose': {
          sceneId: 's1',
          body: 'Server copy.\n',
          version: 9,
          updatedAt: '2026-10-23T00:00:00Z',
        },
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'My edit.\n' });

    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    const set = page.sent().find((m) => m.type === 'setScene');
    expect(set).toMatchObject({ scene: { id: 's1', prose: 'Server copy.\n', version: 9 } });
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1);
    await waitFor(() => expect(saveDot()).toBe('Save status: saved'));
  });

  it('Save_WhenForbidden_ShouldPutTheLastSavedTextBackAndReadTheHeaderAgain', async () => {
    const { app, page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': {
          status: 403,
          body: { error: 'You can no longer edit this book.', code: 'forbidden' },
        },
      },
    });
    const headerReads = () => app.api.callsTo(`GET /api/books/${FAKE_BOOK_ID}`).length;
    const before = headerReads();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Refused.\n' });

    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    expect(page.sent().find((m) => m.type === 'setScene')).toMatchObject({
      scene: { id: 's1', prose: 'First.\n', version: 1 },
    });
    await waitFor(() => expect(headerReads()).toBeGreaterThan(before));
  });

  it('Save_WhenTheServerFails_ShouldKeepTheEditAndShowRetryOnTheDot', async () => {
    const { page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': {
          status: 500,
          body: { error: 'Down.', code: 'internal_error' },
        },
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Kept.\n' });

    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(saveDot()).toBe('Save status: retry'));
  });
});

describe('Write: block operations', () => {
  it('BeatAction_ShouldSaveFirstThenInsertTheBeatAtTheSavedVersionAndSetTheScene', async () => {
    let inserted = false;
    const { app, page } = await readyChapterOne({
      api: {
        'POST /api/scenes/s1/beats': () => {
          inserted = true;
          return { beatId: 'beat-new', version: 3 };
        },
        [`GET /api/books/${FAKE_BOOK_ID}/beats`]: () => ({
          bookId: FAKE_BOOK_ID,
          beats: inserted ? [beat({ id: 'beat-new', body: 'A new beat.' })] : [],
        }),
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'First, typed.\n' });

    await page.say({
      type: 'action',
      sceneId: 's1',
      action: 'beat',
      beatId: 'beat-new',
      body: 'First, typed.\n\n::beat{id="beat-new"}\n',
    });

    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1);
    expect(app.api.callsTo('POST /api/scenes/s1/beats')[0]?.body).toEqual({
      beatId: 'beat-new',
      body: 'First, typed.\n\n::beat{id="beat-new"}\n',
      version: 2,
    });
    expect(page.sent().find((m) => m.type === 'setScene')).toMatchObject({
      scene: {
        id: 's1',
        prose: 'First, typed.\n\n::beat{id="beat-new"}\n',
        version: 3,
        beats: [{ id: 'beat-new', body: 'A new beat.' }],
      },
    });
  });

  it('BeatAction_WhenTheServerAnswers409_ShouldTellTheWriterAndGiveThePageTheServersCopy', async () => {
    const { page } = await readyChapterOne({
      api: {
        'POST /api/scenes/s1/beats': conflict,
        'GET /api/scenes/s1/prose': {
          sceneId: 's1',
          body: 'Server copy.\n',
          version: 9,
          updatedAt: '2026-10-23T00:00:00Z',
        },
      },
    });

    await page.say({
      type: 'action',
      sceneId: 's1',
      action: 'beat',
      beatId: 'b',
      body: 'x\n\n::beat{id="b"}\n',
    });

    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent('That scene changed.');
    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    expect(page.sent().find((m) => m.type === 'setScene')).toMatchObject({
      scene: { id: 's1', prose: 'Server copy.\n', version: 9 },
    });
  });

  it('NewSceneAction_ShouldSplitTheSceneThenLoadTheChapterWithTheNewSceneInView', async () => {
    let split = false;
    const withNewScene = () => {
      const outline = writeOutline();
      const chapter = outline.acts[0]!.chapters[0]!;
      chapter.scenes = [
        chapter.scenes[0]!,
        { ...chapter.scenes[0]!, id: 's9', number: '1.2', title: 'Split off' },
        { ...chapter.scenes[1]!, number: '1.3' },
      ];
      return outline;
    };
    const { app, page } = await readyChapterOne({
      api: {
        'POST /api/scenes/s1/split': () => {
          split = true;
          return { newSceneId: 's9', version: 3, newSceneVersion: 1 };
        },
        [`GET /api/books/${FAKE_BOOK_ID}/outline`]: () => (split ? withNewScene() : writeOutline()),
      },
    });

    await page.say({
      type: 'action',
      sceneId: 's1',
      action: 'newScene',
      headBody: 'Before.\n',
      tailBody: 'After.\n',
    });

    await page.waitForLoads(2);
    expect(app.api.callsTo('POST /api/scenes/s1/split')[0]?.body).toEqual({
      headBody: 'Before.\n',
      tailBody: 'After.\n',
      version: 1,
    });
    const load = page.loads()[1]!;
    expect(load.scenes.map((s) => s.id)).toEqual(['s1', 's9', 's2']);
    expect(load.scrollToSceneId).toBe('s9');
    expect(load.scenes[0]).toMatchObject({ prose: 'Before.\n', version: 3 });
    expect(load.scenes[1]).toMatchObject({ prose: 'After.\n', version: 1 });
  });
});

describe('Write: refused saves', () => {
  const refused = (status: number, error: string, code: string) => {
    let calls = 0;
    return () => {
      calls += 1;
      return calls === 1 ? { status, body: { error, code } } : { version: 2 };
    };
  };

  it('Save_WhenTheServerAnswers400_ShouldShowItsMessageAndClearItOnTheNextSavedEdit', async () => {
    const { page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': refused(400, 'That text is not allowed here.', 'validation'),
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Odd.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });
    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent(
      'That text is not allowed here.',
    );

    await page.say({ type: 'change', sceneId: 's1', markdown: 'Fine.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(screen.queryByTestId(writeTestIds.notice)).toBeNull());
  });

  it('Save_WhenTheServerAnswers403_ShouldShowItsMessage', async () => {
    const { page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': refused(403, 'You can no longer edit this book.', 'forbidden'),
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Nope.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });

    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent(
      'You can no longer edit this book.',
    );
  });

  it('Notice_ShouldGoWhenDismissed', async () => {
    const { page } = await readyChapterOne();
    await page.say({ type: 'error', message: 'Something odd.' });
    expect(await screen.findByTestId(writeTestIds.notice)).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId(writeTestIds.noticeDismiss));

    expect(screen.queryByTestId(writeTestIds.notice)).toBeNull();
  });

  it('Save_WhenTheServerBumpsTheVersion_ShouldSendTheNewVersionNext', async () => {
    let calls = 0;
    const { app, page } = await readyChapterOne({
      api: {
        'PUT /api/scenes/s1/prose': () => {
          calls += 1;
          return { version: calls === 1 ? 7 : 8 };
        },
      },
    });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'One.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });
    await waitFor(() => expect(calls).toBe(1));

    await page.say({ type: 'change', sceneId: 's1', markdown: 'Two.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });

    await waitFor(() => expect(calls).toBe(2));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose').map((c) => c.body)).toEqual([
      { body: 'One.\n', version: 1 },
      { body: 'Two.\n', version: 7 },
    ]);
  });
});
