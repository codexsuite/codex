import { describe, expect, it, vi } from 'vitest';
import { SpeechEngineError } from '../speech/SpeechEngine';
import { createFakeSpeechEngine } from './fakeSpeechEngine';

describe('fake speech engine', () => {
  it('Download_WhenFinished_ShouldInstallAndActivateTheFirstModel', async () => {
    const engine = createFakeSpeechEngine();
    const onProgress = vi.fn();
    const listener = vi.fn();
    engine.subscribe(listener);

    const download = engine.download('base.en', onProgress);
    engine.downloadOf('base.en').progress(1000);
    engine.downloadOf('base.en').finish();
    await download.done;

    expect(onProgress).toHaveBeenCalledWith({ receivedBytes: 1000, totalBytes: 147_964_211 });
    expect(await engine.installed()).toEqual([{ id: 'base.en', sizeBytes: 147_964_211 }]);
    expect(await engine.activeModelId()).toBe('base.en');
    expect(listener).toHaveBeenCalled();
  });

  it('Download_WhenCancelled_ShouldRejectWithCancelled', async () => {
    const engine = createFakeSpeechEngine();
    const download = engine.download('tiny.en', vi.fn());

    await download.cancel();

    await expect(download.done).rejects.toMatchObject({ code: 'cancelled' });
    expect(await engine.installed()).toEqual([]);
  });

  it('Download_WhenItFails_ShouldRejectWithDownloadFailed', async () => {
    const engine = createFakeSpeechEngine();
    const download = engine.download('tiny.en', vi.fn());

    engine.downloadOf('tiny.en').fail('offline');

    await expect(download.done).rejects.toThrow(
      new SpeechEngineError('download-failed', 'offline'),
    );
  });

  it('SetActiveModel_WhenNotInstalled_ShouldReject', async () => {
    const engine = createFakeSpeechEngine();

    await expect(engine.setActiveModel('tiny.en')).rejects.toMatchObject({ code: 'not-installed' });
  });

  it('Delete_OfTheActiveModel_ShouldLeaveNoneActive', async () => {
    const engine = createFakeSpeechEngine({ installed: ['tiny.en'], active: 'tiny.en' });

    await engine.delete('tiny.en');

    expect(await engine.activeModelId()).toBeNull();
    expect(await engine.installed()).toEqual([]);
  });

  it('Transcribe_ShouldReturnTheTranscriptAndRecordTheFile', async () => {
    const engine = createFakeSpeechEngine({
      installed: ['tiny.en'],
      active: 'tiny.en',
      transcript: 'Hello.',
    });

    await expect(engine.transcribe('file:///a.wav')).resolves.toBe('Hello.');
    expect(engine.transcribed).toEqual(['file:///a.wav']);
  });

  it('Transcribe_WithoutAnActiveModel_ShouldReject', async () => {
    const engine = createFakeSpeechEngine();

    await expect(engine.transcribe('file:///a.wav')).rejects.toMatchObject({
      code: 'no-active-model',
    });
  });

  it('Transcribe_AfterFailNext_ShouldRejectOnceThenRecover', async () => {
    const engine = createFakeSpeechEngine({ installed: ['tiny.en'], active: 'tiny.en' });
    engine.failNextTranscription('bad audio');

    await expect(engine.transcribe('file:///a.wav')).rejects.toMatchObject({
      code: 'transcribe-failed',
    });
    await expect(engine.transcribe('file:///a.wav')).resolves.toBe('This is the fake transcript.');
  });
});
