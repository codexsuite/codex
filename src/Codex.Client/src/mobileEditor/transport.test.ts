import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  encodeHostMessage,
  type HostMessage,
  type PageMessage,
} from '@codex/core/editing/editorBridge';
import { onHostMessage, sendToHost } from './transport';

describe('mobile editor transport', () => {
  const posted: string[] = [];

  beforeEach(() => {
    posted.length = 0;
    window.ReactNativeWebView = { postMessage: (message) => void posted.push(message) };
  });
  afterEach(() => {
    delete window.ReactNativeWebView;
  });

  const parsed = (): PageMessage[] => posted.map((raw) => JSON.parse(raw) as PageMessage);

  it('sendToHost_WhenInTheApp_ShouldPostTheEncodedMessage', () => {
    sendToHost({ type: 'blur', sceneId: 's1' });

    expect(parsed()).toEqual([{ type: 'blur', sceneId: 's1' }]);
  });

  it('sendToHost_WhenInAPlainBrowser_ShouldDoNothing', () => {
    delete window.ReactNativeWebView;

    expect(() => sendToHost({ type: 'blur', sceneId: 's1' })).not.toThrow();
  });

  it.each([
    ['window', window],
    ['document', document],
  ])('onHostMessage_WhenTheMessageArrivesOn%s_ShouldPassItOn', (_name, target) => {
    const handler = vi.fn<(message: HostMessage) => void>();
    const stop = onHostMessage(handler);
    const message: HostMessage = { type: 'scrollTo', sceneId: 's2' };

    target.dispatchEvent(new MessageEvent('message', { data: encodeHostMessage(message) }));
    stop();

    expect(handler).toHaveBeenCalledExactlyOnceWith(message);
  });

  it('onHostMessage_WhenTheMessageIsInvalid_ShouldAnswerWithAnError', () => {
    const handler = vi.fn();
    const stop = onHostMessage(handler);

    window.dispatchEvent(new MessageEvent('message', { data: '{"type":"explode"}' }));
    stop();

    expect(handler).not.toHaveBeenCalled();
    expect(parsed()).toEqual([
      { type: 'error', message: 'Rejected a message from the app: unknown message type "explode"' },
    ]);
  });

  it('onHostMessage_WhenStopped_ShouldIgnoreLaterMessages', () => {
    const handler = vi.fn();
    onHostMessage(handler)();

    window.dispatchEvent(
      new MessageEvent('message', { data: encodeHostMessage({ type: 'scrollTo', sceneId: 's' }) }),
    );

    expect(handler).not.toHaveBeenCalled();
  });
});
