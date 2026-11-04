import { memoryStore } from '@codex/core/platform';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { activeModelKey, createSpeechEngine } from './createSpeechEngine';
import { MemoryFiles, MemoryWhisper } from './test-support/memorySpeechRuntime';
import { SpeechEngineError, type SpeechEngine } from './SpeechEngine';
import { findSpeechModel } from './speechModelCatalog';

const tiny = findSpeechModel('tiny.en')!;
const base = findSpeechModel('base.en')!;
const multilingual = findSpeechModel('base')!;

let files: MemoryFiles;
let whisper: MemoryWhisper;
let store: ReturnType<typeof memoryStore>;
let engine: SpeechEngine;

beforeEach(() => {
  files = new MemoryFiles();
  whisper = new MemoryWhisper();
  store = memoryStore();
  engine = createSpeechEngine({ files, whisper, store: () => store });
});

const install = (...models: (typeof tiny)[]) =>
  models.forEach((model) => files.sizes.set(model.fileName, model.sizeBytes));

describe('installed', () => {
  it('ShouldOnlyListCompleteFiles', async () => {
    install(tiny);
    files.sizes.set(base.fileName, 10);
    expect(await engine.installed()).toEqual([{ id: 'tiny.en', sizeBytes: tiny.sizeBytes }]);
  });
});

describe('download', () => {
  it('Progress_WhenTheServerSendsNoLength_ShouldReportTheCatalogSize', async () => {
    const onProgress = vi.fn();
    const download = engine.download('tiny.en', onProgress);
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.options.onProgress(1000, -1);
    files.pending[0]!.finish(tiny.sizeBytes);
    await download.done;

    expect(onProgress).toHaveBeenCalledWith({ receivedBytes: 1000, totalBytes: tiny.sizeBytes });
  });

  it('Finished_ShouldWriteToAPartialNameThenRenameAndActivateTheFirstModel', async () => {
    const listener = vi.fn();
    engine.subscribe(listener);
    const download = engine.download('base.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));
    expect(files.pending[0]).toMatchObject({ url: base.url, name: 'ggml-base.en.bin.part' });
    expect(await engine.installed()).toEqual([]);

    files.pending[0]!.finish(base.sizeBytes);
    await download.done;

    expect([...files.sizes.keys()]).toEqual([base.fileName]);
    expect(await engine.installed()).toEqual([{ id: 'base.en', sizeBytes: base.sizeBytes }]);
    expect(await engine.activeModelId()).toBe('base.en');
    expect(listener).toHaveBeenCalled();
  });

  it('Finished_WhenAnotherModelIsActive_ShouldKeepIt', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    const download = engine.download('base.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.finish(base.sizeBytes);
    await download.done;

    expect(await engine.activeModelId()).toBe('tiny.en');
  });

  it('Finished_WhenTheFileIsShort_ShouldFailAndLeaveNothingBehind', async () => {
    const download = engine.download('base.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.finish(1234);

    await expect(download.done).rejects.toMatchObject({ code: 'download-failed' });
    expect([...files.sizes.keys()]).toEqual([]);
    expect(await engine.activeModelId()).toBeNull();
  });

  it('Failing_ShouldRejectWithDownloadFailedAndRemoveThePartialFile', async () => {
    const download = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.fail(new Error('offline'));

    await expect(download.done).rejects.toThrow(
      new SpeechEngineError('download-failed', 'offline'),
    );
    expect(files.sizes.size).toBe(0);
  });

  it('Cancel_ShouldAbortRejectWithCancelledAndRemoveThePartialFile', async () => {
    const download = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    await download.cancel();

    await expect(download.done).rejects.toMatchObject({ code: 'cancelled' });
    expect(files.pending[0]!.options.signal.aborted).toBe(true);
    expect(files.sizes.size).toBe(0);
  });

  it('WhenAskedTwiceForTheSameModel_ShouldShareOneDownload', async () => {
    const first = engine.download('tiny.en', vi.fn());
    const second = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    expect(second).toBe(first);
    files.pending[0]!.finish(tiny.sizeBytes);
    await first.done;
  });

  it('WhenAlreadyInstalled_ShouldDoNothing', async () => {
    install(tiny);

    await engine.download('tiny.en', vi.fn()).done;

    expect(files.pending).toHaveLength(0);
  });

  it('WithAnUnknownModel_ShouldThrow', () => {
    expect(() => engine.download('huge', vi.fn())).toThrow(SpeechEngineError);
  });
});

describe('download state kept by the engine', () => {
  it('Progress_ShouldBeReadableAndAnnouncedWithoutTheOriginalCallback', async () => {
    const listener = vi.fn();
    engine.subscribeDownloads(listener);
    const download = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.options.onProgress(500, tiny.sizeBytes);

    expect(engine.downloads()).toEqual({
      'tiny.en': { status: 'downloading', receivedBytes: 500, totalBytes: tiny.sizeBytes },
    });
    expect(listener).toHaveBeenCalled();
    files.pending[0]!.finish(tiny.sizeBytes);
    await download.done;
    expect(engine.downloads()).toEqual({});
  });

  it('AskingAgainWhileDownloading_ShouldCallTheNewProgressCallbackToo', async () => {
    const first = vi.fn();
    const second = vi.fn();
    const download = engine.download('tiny.en', first);
    engine.download('tiny.en', second);
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.options.onProgress(500, tiny.sizeBytes);

    expect(second).toHaveBeenCalledWith({ receivedBytes: 500, totalBytes: tiny.sizeBytes });
    expect(first).toHaveBeenCalledTimes(1);
    files.pending[0]!.finish(tiny.sizeBytes);
    await download.done;
  });

  it('Failure_ShouldStayUntilTheModelIsDownloadedAgain', async () => {
    const download = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));
    files.pending[0]!.fail(new Error('offline'));
    await download.done.catch(() => undefined);

    expect(engine.downloads()).toEqual({ 'tiny.en': { status: 'failed', message: 'offline' } });

    engine.download('tiny.en', vi.fn());
    expect(engine.downloads()['tiny.en']).toMatchObject({ status: 'downloading' });
    await vi.waitFor(() => expect(files.pending).toHaveLength(2));
    files.pending[1]!.finish(tiny.sizeBytes);
  });

  it('Cancel_ShouldLeaveNoDownloadState', async () => {
    engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    await engine.cancelDownload('tiny.en');

    expect(engine.downloads()).toEqual({});
  });

  it('DeletingAnInstalledModelAfterDownloadingItAgain_ShouldAllowANewDownload', async () => {
    install(tiny);
    await engine.download('tiny.en', vi.fn()).done;
    await engine.delete('tiny.en');

    const again = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    files.pending[0]!.finish(tiny.sizeBytes);
    await again.done;
    expect(await engine.installed()).toHaveLength(1);
  });
});

describe('stale partial files', () => {
  it('Installed_ShouldRemovePartFilesNobodyIsDownloading', async () => {
    files.sizes.set('ggml-small.en.bin.part', 999);
    install(tiny);

    expect(await engine.installed()).toHaveLength(1);

    expect([...files.sizes.keys()]).toEqual([tiny.fileName]);
  });
});

describe('delete and choose', () => {
  it('Delete_OfTheActiveModel_ShouldRemoveItsFileAndLeaveNoneActive', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    const listener = vi.fn();
    engine.subscribe(listener);

    await engine.delete('tiny.en');

    expect(files.sizes.size).toBe(0);
    expect(await engine.activeModelId()).toBeNull();
    expect(store.getItem(activeModelKey)).toBeNull();
    expect(listener).toHaveBeenCalled();
  });

  it('Delete_OfAModelBeingDownloaded_ShouldCancelIt', async () => {
    const download = engine.download('tiny.en', vi.fn());
    await vi.waitFor(() => expect(files.pending).toHaveLength(1));

    await engine.delete('tiny.en');

    await expect(download.done).rejects.toMatchObject({ code: 'cancelled' });
  });

  it('SetActiveModel_WhenNotInstalled_ShouldReject', async () => {
    await expect(engine.setActiveModel('tiny.en')).rejects.toMatchObject({ code: 'not-installed' });
  });

  it('ActiveModelId_WhenTheStoredModelFileIsGone_ShouldBeNull', async () => {
    store.setItem(activeModelKey, 'tiny.en');
    expect(await engine.activeModelId()).toBeNull();
  });

  it('SetActiveModel_ShouldPersistTheChoice', async () => {
    install(tiny, base);
    await engine.setActiveModel('base.en');
    const reopened = createSpeechEngine({ files, whisper, store: () => store });
    expect(await reopened.activeModelId()).toBe('base.en');
  });
});

describe('transcribe', () => {
  it('WithoutAnActiveModel_ShouldReject', async () => {
    await expect(engine.transcribe('file:///a.wav')).rejects.toMatchObject({
      code: 'no-active-model',
    });
  });

  it('ShouldOpenTheModelFileOnceAndReuseItAndTrimTheText', async () => {
    install(base);
    await engine.setActiveModel('base.en');
    whisper.answers = ['  First. ', 'Second.'];

    expect(await engine.transcribe('file:///a.wav')).toBe('First.');
    expect(await engine.transcribe('file:///b.wav')).toBe('Second.');

    expect(whisper.sessions).toHaveLength(1);
    expect(whisper.sessions[0]!.modelUri).toBe('file:///models/ggml-base.en.bin');
    expect(whisper.sessions[0]!.calls).toEqual([
      { audioFileUri: 'file:///a.wav', language: 'en' },
      { audioFileUri: 'file:///b.wav', language: 'en' },
    ]);
  });

  it('WithAMultilingualModel_ShouldDetectTheLanguage', async () => {
    install(multilingual);
    await engine.setActiveModel('base');

    await engine.transcribe('file:///a.wav');

    expect(whisper.sessions[0]!.calls[0]!.language).toBe('auto');
  });

  it('SwitchingModel_ShouldReleaseTheOldContext', async () => {
    install(tiny, base);
    await engine.setActiveModel('tiny.en');
    await engine.transcribe('file:///a.wav');

    await engine.setActiveModel('base.en');
    await engine.transcribe('file:///a.wav');

    expect(whisper.sessions.map((s) => s.released)).toEqual([true, false]);
  });

  it('DeletingTheActiveModel_ShouldReleaseTheContext', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    await engine.transcribe('file:///a.wav');

    await engine.delete('tiny.en');

    expect(whisper.sessions[0]!.released).toBe(true);
  });

  it('WhenWhisperFails_ShouldRejectWithTranscribeFailedAndOpenAFreshContextNextTime', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    whisper.answers = [new Error('bad wav'), 'fine'];

    await expect(engine.transcribe('file:///a.wav')).rejects.toThrow(
      new SpeechEngineError('transcribe-failed', 'bad wav'),
    );
    expect(whisper.sessions[0]!.released).toBe(true);
    expect(await engine.transcribe('file:///a.wav')).toBe('fine');
    expect(whisper.sessions).toHaveLength(2);
    expect(whisper.sessions[1]!.released).toBe(false);
  });

  it('WhenAborted_ShouldStopWhisperAndRejectWithCancelled', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    whisper.answers = ['hang'];
    const controller = new AbortController();

    const result = engine.transcribe('file:///a.wav', { signal: controller.signal });
    await vi.waitFor(() => expect(whisper.sessions[0]?.calls).toHaveLength(1));
    controller.abort();

    await expect(result).rejects.toMatchObject({ code: 'cancelled' });
    expect(whisper.sessions[0]!.stopped).toBe(1);
  });

  it('WhenAbortedWhileTheModelOpens_ShouldRejectWithCancelledBeforeTranscribing', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    const controller = new AbortController();

    const result = engine.transcribe('file:///a.wav', { signal: controller.signal });
    controller.abort();

    await expect(result).rejects.toMatchObject({ code: 'cancelled' });
    expect(whisper.sessions[0]?.calls ?? []).toEqual([]);
  });

  it('WhenAlreadyAborted_ShouldRejectWithoutOpeningTheModel', async () => {
    install(tiny);
    await engine.setActiveModel('tiny.en');
    const controller = new AbortController();
    controller.abort();

    await expect(
      engine.transcribe('file:///a.wav', { signal: controller.signal }),
    ).rejects.toMatchObject({
      code: 'cancelled',
    });
    expect(whisper.sessions).toHaveLength(0);
  });
});
