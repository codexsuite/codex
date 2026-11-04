import {
  decodeHostMessage,
  encodePageMessage,
  type HostMessage,
  type PageMessage,
} from '@codex/core/editing/editorBridge';

// The page's side of the bridge (#35): the one place that touches `window.ReactNativeWebView` and the `message`
// events the app's `postMessage` produces. react-native-webview delivers them on `window` on iOS and on `document`
// on Android, so both are listened to. The protocol itself is `@codex/core/editing/editorBridge`.

/** Sends a message to the app. Outside the app (a plain browser tab) there is nobody to hear it. */
export function sendToHost(message: PageMessage): void {
  window.ReactNativeWebView?.postMessage(encodePageMessage(message));
}

/**
 * Calls `handler` with every valid message from the app. A message that does not decode is answered with an `error`
 * message to the app and not passed on. Returns the unsubscribe.
 */
export function onHostMessage(handler: (message: HostMessage) => void): () => void {
  const listener = (event: Event) => {
    const decoded = decodeHostMessage((event as MessageEvent).data);
    if (decoded.ok) handler(decoded.message);
    else
      sendToHost({ type: 'error', message: `Rejected a message from the app: ${decoded.reason}` });
  };
  window.addEventListener('message', listener);
  document.addEventListener('message', listener);
  return () => {
    window.removeEventListener('message', listener);
    document.removeEventListener('message', listener);
  };
}
