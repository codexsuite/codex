import type { NativeSyntheticEvent } from 'react-native';
import type {
  ShouldStartLoadRequest,
  WebViewError,
  WebViewHttpError,
  WebViewMessage,
  WebViewNativeEvent,
} from 'react-native-webview/lib/WebViewTypes';
import { editorPageUrl } from '../pageGuards';

// Typed events for driving the WebView props a spec calls by hand (the mock renders no real WebView).

/** A React Native synthetic event around `nativeEvent`. */
export function syntheticEvent<T>(nativeEvent: T): NativeSyntheticEvent<T> {
  return {
    nativeEvent,
    currentTarget: 0,
    dispatchConfig: { registrationName: '' },
    target: 0,
    bubbles: false,
    cancelable: false,
    defaultPrevented: false,
    eventPhase: 0,
    isTrusted: false,
    timeStamp: 0,
    type: '',
    preventDefault: () => {},
    isDefaultPrevented: () => false,
    stopPropagation: () => {},
    isPropagationStopped: () => false,
    persist: () => {},
  };
}

const location = (url: string): WebViewNativeEvent => ({
  url,
  loading: false,
  title: '',
  canGoBack: false,
  canGoForward: false,
  lockIdentifier: 0,
});

export const messageEvent = (data: string, url: string = editorPageUrl()) =>
  syntheticEvent<WebViewMessage>({ ...location(url), data });

export const httpErrorEvent = (statusCode: number) =>
  syntheticEvent<WebViewHttpError>({ ...location(editorPageUrl()), description: '', statusCode });

export const errorEvent = (description: string) =>
  syntheticEvent<WebViewError>({ ...location(editorPageUrl()), code: -1, description });

export const loadRequest = (url: string): ShouldStartLoadRequest => ({
  ...location(url),
  navigationType: 'click',
  mainDocumentURL: url,
  isTopFrame: true,
});
