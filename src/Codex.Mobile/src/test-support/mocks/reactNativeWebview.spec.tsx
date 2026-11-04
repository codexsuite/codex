import { EDITOR_BRIDGE_VERSION, encodeHostMessage } from '@codex/core/editing/editorBridge';
import { createRef } from 'react';
import { renderScreen } from '../renderScreen';
import { WebView, webViewHarness } from './reactNativeWebview';

describe('react-native-webview mock', () => {
  it('Ref_PostMessage_ShouldRecordWhatTheAppSentAndDecodeIt', async () => {
    const ref = createRef<{ postMessage(data: string): void }>();
    await renderScreen(<WebView ref={ref} testID="web" />);

    ref.current?.postMessage(encodeHostMessage({ type: 'scrollTo', sceneId: 's1' }));

    expect(webViewHarness.last().sentToPage()).toEqual([{ type: 'scrollTo', sceneId: 's1' }]);
  });

  it('SentToPage_WhenTheAppSentSomethingInvalid_ShouldThrow', async () => {
    const ref = createRef<{ postMessage(data: string): void }>();
    await renderScreen(<WebView ref={ref} />);

    ref.current?.postMessage('{"type":"explode"}');

    expect(() => webViewHarness.last().sentToPage()).toThrow('invalid message');
  });

  it('EmitFromPage_ShouldCallOnMessageWithTheEncodedMessage', async () => {
    const onMessage = jest.fn();
    await renderScreen(<WebView onMessage={onMessage} />);

    await webViewHarness.last().emitFromPage({ type: 'ready', v: EDITOR_BRIDGE_VERSION });

    expect(onMessage).toHaveBeenCalledWith(
      expect.objectContaining({
        nativeEvent: expect.objectContaining({ data: '{"type":"ready","v":1}' }),
      }),
    );
  });

  it('Harness_AfterTheTest_ShouldForgetUnmountedWebViews', async () => {
    const { unmount } = await renderScreen(<WebView />);
    expect(webViewHarness.all()).toHaveLength(1);

    await unmount();

    expect(webViewHarness.all()).toHaveLength(0);
  });
});
