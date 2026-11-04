import {
  decodeHostMessage,
  encodePageMessage,
  type HostMessage,
  type PageMessage,
} from '@codex/core/editing/editorBridge';
import { act } from '@testing-library/react-native';
import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import { View } from 'react-native';
import type { WebViewMessageEvent, WebViewProps } from 'react-native-webview';

// A stand-in for react-native-webview (registered in jestSetup.ts). It renders an empty View, records what the app
// sends to the page, and lets a spec play the page: `await webView.emitFromPage({ type: 'ready', v: 1 })`.

export interface MockWebView {
  /** The props of the latest render (`source`, `onMessage`, …). */
  props: WebViewProps;
  /** Every string passed to `ref.postMessage`, in order. */
  posted: string[];
  /** Every script passed to `ref.injectJavaScript`, in order. */
  injected: string[];
  /** What the app sent to the page, decoded; a string that does not decode throws, so a bad message fails the spec. */
  sentToPage(): HostMessage[];
  /** The page says something to the app (calls the `onMessage` prop inside `act`). */
  emitFromPage(message: PageMessage): Promise<void>;
  /** The page sends text that is not a valid message. */
  emitRawFromPage(data: string): Promise<void>;
}

const instances: MockWebView[] = [];

export const webViewHarness = {
  /** The WebView mounted last. Throws when none is mounted. */
  last(): MockWebView {
    const last = instances[instances.length - 1];
    if (!last) throw new Error('No WebView has been mounted.');
    return last;
  },
  all: (): readonly MockWebView[] => instances,
  reset(): void {
    instances.length = 0;
  },
};

function createInstance(): MockWebView {
  const instance: MockWebView = {
    props: {},
    posted: [],
    injected: [],
    sentToPage() {
      return instance.posted.map((raw) => {
        const decoded = decodeHostMessage(raw);
        if (!decoded.ok)
          throw new Error(`The app sent the page an invalid message: ${decoded.reason}`);
        return decoded.message;
      });
    },
    emitFromPage: (message) => instance.emitRawFromPage(encodePageMessage(message)),
    async emitRawFromPage(data) {
      await act(async () => {
        instance.props.onMessage?.({
          nativeEvent: {
            data,
            url: 'about:blank',
            loading: false,
            title: '',
            canGoBack: false,
            canGoForward: false,
            lockIdentifier: 0,
          },
        } as WebViewMessageEvent);
      });
    },
  };
  return instance;
}

export const WebView = forwardRef<unknown, WebViewProps>(function MockedWebView(props, ref) {
  const instance = useRef<MockWebView | null>(null);
  instance.current ??= createInstance();
  instance.current.props = props;

  useEffect(() => {
    const mounted = instance.current;
    if (mounted) instances.push(mounted);
    return () => {
      const at = instances.indexOf(mounted as MockWebView);
      if (at >= 0) instances.splice(at, 1);
    };
  }, []);

  useImperativeHandle(ref, () => ({
    postMessage: (data: string) => void instance.current?.posted.push(data),
    injectJavaScript: (script: string) => void instance.current?.injected.push(script),
    reload: () => {},
    stopLoading: () => {},
    goBack: () => {},
    goForward: () => {},
    requestFocus: () => {},
  }));

  return <View testID={props.testID} />;
});

export default WebView;
